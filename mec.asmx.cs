using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Services;
using System.Xml;
using System.Xml.XPath;
//using System.IO.Packaging;
//using Ionic.Zlib;

namespace MECws
{
    /// <summary>
    /// Summary description for mec
    /// </summary>
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [System.ComponentModel.ToolboxItem(false)]
    // To allow this Web Service to be called from script, using ASP.NET AJAX, uncomment the following line. 
    // [System.Web.Script.Services.ScriptService]
    public class mec : System.Web.Services.WebService
    {

        [WebMethod]
        public string data(Input input)
        {

            byte[] bytes = Convert.FromBase64String(input.base64);

            Stream zipStream = new MemoryStream(bytes);

            // ZipInputStream zipInputStream = new ZipInputStream(zipStream);

            //ZipEntry zipEntry = zipInputStream.GetNextEntry();
            //  if (zipEntry != null)
            //  {
            // var sr;
            var myStr = new StreamReader(zipStream).ReadToEnd();

            // var sr = new StreamReader(zipInputStream);
           // var myStr = sr.ReadToEnd();
                myStr = myStr.Replace("ns0:", "").Replace("n0:", "");

                XmlDocument xml = new XmlDocument();
                xml.LoadXml(myStr);
                //string text = zipInputStream.ToString();
                GetSteps(input.mec, xml);
          //  }


            return "true";
        }

        private void GetSteps(string mec, XmlDocument xml)
        {

            XmlDocument document = new XmlDocument();
            document.Load(Server.MapPath("xml\\" + mec + ".xml"));
            XmlNodeList steps = document.SelectNodes("/steps/step");
            string tables = string.Empty;

            foreach (XmlNode step in steps)
            {
                if(step.Attributes["type"]!=null&& step.Attributes["type"].Value.ToLower() == "update")
                {
                    XmlNode field = step.SelectSingleNode("field[@type='WHERE']");
                    tables += (string.IsNullOrEmpty(tables) ? "" : ",") + "'" + field.Attributes["default"].Value + "'";
                }
                    
            }
            string connectionString = ConfigurationManager.ConnectionStrings["localdb"].ConnectionString;
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                SqlTransaction transaction;
                connection.Open();

                var idDoc = xml.ChildNodes[1].SelectSingleNode("CTRL/NumeroIdoc").InnerText;
                var numTienda = xml.ChildNodes[1].SelectSingleNode("CTRL/NumeroTienda").InnerText;
                var messageGuid = xml.ChildNodes[1].SelectSingleNode("CTRL/Guid").InnerText;


                bool idDocExist = false;
                /*using (var cmd = connection.CreateCommand())
                {

                    if (string.IsNullOrEmpty(tables)) tables = "''";
                    cmd.CommandText = "select MD5 from [NPVFRControlTablas]  where NOMBRE IN ("+tables+");";
                    cmd.CommandType = System.Data.CommandType.Text;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string md5 = reader.GetString(0).Trim();
                            long number;
                            bool result = Int64.TryParse(md5, out number);
                            if (result && number >= Int64.Parse(idDoc))
                            {
                                idDocExist = true;
                                break;
                            }
                            
                        }
                    }
                }*/

                if (idDocExist)
                    using (var cmd = connection.CreateCommand())
                    {
                        
                        cmd.CommandText = "insert into NPVFRLOGPUBLICACION values(GETDATE(),'" + mec.Replace("_","") + "','" + numTienda + "','" + messageGuid + "','OMITIDO','Se omite la publicación porque el numero de idoc es inferior')";
                        cmd.CommandType = System.Data.CommandType.Text;
                        cmd.ExecuteNonQuery();

                        return;
                       
                    }

                transaction = connection.BeginTransaction(mec);
                try
                {
                    //messageGuid = string.Empty;
                    foreach (XmlNode step in steps)
                    {
                        string path = step.Attributes["path"].Value;
                        string target = step.Attributes["target"].Value;
                        string type = step.Attributes["type"] == null ? string.Empty : step.Attributes["type"].Value;


                        if (type.ToLower() == "update")
                        {
                            XmlNodeList rows = xml.SelectNodes(path);
                            SQLUpdate(connection, step, target, rows, transaction);
                        }
                        else if (path != "")
                        {
                            XmlNodeList rows = xml.SelectNodes(path);
                            SQLInsert(connection, step, target, rows, transaction);
                        }
                        else
                        {
                            using (SqlCommand command = new SqlCommand(target, connection))
                            {
                                foreach (XmlNode field in step.ChildNodes)
                                {
                                    // <field field="CLAVE" dbtype ="char" size="10"  path="CLAVE"  />
                                    string dbcol = field.Attributes["field"].Value;
                                    string dbtype = field.Attributes["dbtype"].Value;
                                    int size = int.Parse(field.Attributes["size"].Value);
                                    string fieldPath = field.Attributes["path"].Value;

                                    XmlNode val = null;
                                    if (fieldPath != "")
                                        val = xml.SelectSingleNode(fieldPath);
                                    string value = string.Empty;
                                    if (val != null)
                                    {
                                        value = val.InnerText;
                                    }
                                    else
                                    {
                                        XmlAttribute defAt = field.Attributes["default"];
                                        if (defAt == null)
                                        {
                                            throw new XmlException("Element in ROW is missing");
                                        }

                                        value = defAt.Value;
                                        if (defAt.Value == "NULL")
                                            value = "NULL";

                                        if (dbtype.Contains("datetime") && value.Contains("::now"))
                                        {
                                            //7/19/2017  7:42:05 PM
                                            //value = DateTime.Now.ToString("MM/dd/yyyy hhh:mm:ss tt");
                                            value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                                        }

                                    }

                                    if (string.Equals(value, "NULL"))
                                    {
                                        value = null;
                                    }

                                    SqlParameter par = null;



                                    switch (dbtype)
                                    {
                                        case "uniqueidentifier":
                                            par = command.Parameters.AddWithValue(dbcol, new Guid(value));
                                            par.DbType = System.Data.DbType.Guid;
                                            messageGuid = value;
                                            break;
                                        case "bit":
                                            par = command.Parameters.AddWithValue(dbcol, value);
                                            par.DbType = System.Data.DbType.Boolean;

                                            break;
                                        case "int":
                                            par = command.Parameters.AddWithValue(dbcol, value);
                                            par.DbType = System.Data.DbType.Int16;

                                            break;
                                        case "float":
                                            par = command.Parameters.AddWithValue(dbcol, value);
                                            par.DbType = System.Data.DbType.Decimal;

                                            break;
                                        case "char":
                                        case "varchar":
                                        case "nvarchar":
                                            par = command.Parameters.AddWithValue(dbcol, value);
                                            par.DbType = System.Data.DbType.String;
                                            break;

                                    }

                                }
                                command.CommandType = System.Data.CommandType.StoredProcedure;
                                command.Transaction = transaction;
                                command.ExecuteNonQuery();
                            }
                        }
                    }

                    //select * from NPVFRLOGPUBLICACION where NumDocumento = 'B67105FD-FAAA-4BA5-8EB8-7B77CBF0010D' and Estatus = 'NO EXITOSO'
                    using (var cmd = connection.CreateCommand())
                    {
                        //and  [Version] = '" + Version.ToString() + "'
                        cmd.CommandText = "select MotivoError from NPVFRLOGPUBLICACION where NumDocumento = '" + messageGuid + "' and Estatus = 'NO EXITOSO'";
                        cmd.CommandType = System.Data.CommandType.Text;
                        cmd.Transaction = transaction;
                        string MotivoError = null;

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                MotivoError = reader.GetString(0);
                            }
                        }
                        if (MotivoError != null)
                        {
                            try
                            {
                                transaction.Rollback();
                            }
                            catch (Exception exRollback)
                            {
                                MotivoError += " No se completó el proceso de Rollback. " + exRollback.Message;
                            }
                            throw new InvalidOperationException(MotivoError);
                        }
                    }

                    transaction.Commit();

                }
                catch (InvalidOperationException e)
                {

                    throw e;
                }
                catch (Exception e)
                {
                    transaction.Rollback();
                    throw e;
                }
            }
        }

        private static void SQLInsert(SqlConnection connection, XmlNode step, string target, XmlNodeList rows, SqlTransaction trans)
        {
            foreach (XmlNode row in rows)
            {
                string insHeader = "INSERT INTO [dbo].[" + target + "](";
                string insValues = string.Empty;

                foreach (XmlNode field in step.ChildNodes)
                {
                    // <field field="CLAVE" dbtype ="char" size="10"  path="CLAVE"  />
                    string dbcol = field.Attributes["field"].Value;
                    string dbtype = field.Attributes["dbtype"].Value;
                    int size = int.Parse(field.Attributes["size"].Value);
                    string fieldPath = field.Attributes["path"].Value;

                    XmlNode val = null;
                    if (fieldPath != "")
                        val = row.SelectSingleNode(fieldPath);
                    string value = string.Empty;
                    if (val != null)
                    {
                        value = val.InnerText;
                    }
                    else
                    {
                        XmlAttribute defAt = field.Attributes["default"];
                        if (defAt == null)
                        {
                            throw new XmlException("Element in ROW is missing");
                        }

                        value = defAt.Value;
                        if (defAt.Value == "NULL")
                            value = "NULL";

                        if (dbtype.Contains("datetime") && value.Contains("::now"))
                        {
                            //7/19/2017  7:42:05 PM
                            //value = DateTime.Now.ToString("MM/dd/yyyy hhh:mm:ss tt");
                            value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                        }

                    }

                    if ("bit,int,float".IndexOf(dbtype) == -1 && value != "NULL")
                    {
                        value = "'" + value.Replace("'", "''") + "'";
                    }

                    if (insValues == string.Empty)
                    {
                        insValues = "VALUES(" + value;
                        insHeader += "[" + dbcol + "]";
                    }
                    else
                    {
                        insValues += "," + value;
                        insHeader += ",[" + dbcol + "]";
                    }
                }
                string commandText = insHeader + ")" + insValues + ");";
                using (SqlCommand command = new SqlCommand(commandText, connection))
                {
                    command.Transaction = trans;
                    int res = command.ExecuteNonQuery();
                    if (res <= 0)
                    {
                        throw new Exception("SQL Command was no affected (" + commandText + ")");
                    }
                }

            }
        }


        private static void SQLUpdate(SqlConnection connection, XmlNode step, string target, XmlNodeList rows, SqlTransaction trans)
        {
            foreach (XmlNode row in rows)
            {
                string updHeader = "UPDATE [" + target + "] ";
                string updValues = string.Empty;
                string updWhere = string.Empty;

                foreach (XmlNode field in step.ChildNodes)
                {
                    // <field field="CLAVE" dbtype ="char" size="10"  path="CLAVE"  />
                    string type = field.Attributes["type"] != null ? field.Attributes["type"].Value : string.Empty;
                    string dbcol = field.Attributes["field"].Value;
                    string dbtype = field.Attributes["dbtype"].Value;
                    int size = int.Parse(field.Attributes["size"].Value);
                    string fieldPath = field.Attributes["path"].Value;

                    XmlNode val = null;
                    if (fieldPath != "")
                        val = row.SelectSingleNode(fieldPath);
                    string value = string.Empty;
                    if (val != null)
                    {
                        value = val.InnerText;
                    }
                    else
                    {
                        XmlAttribute defAt = field.Attributes["default"];
                        if (defAt == null)
                        {
                            throw new XmlException("Element in ROW is missing");
                        }

                        value = defAt.Value;
                        if (defAt.Value == "NULL")
                            value = "NULL";

                        if (dbtype.Contains("datetime") && value.Contains("::now"))
                        {
                            //7/19/2017  7:42:05 PM
                            //value = DateTime.Now.ToString("MM/dd/yyyy hhh:mm:ss tt");
                            value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                        }

                    }

                    if ("bit,int,float".IndexOf(dbtype) == -1 && value != "NULL")
                    {
                        value = "'" + value.Replace("'", "''") + "'";
                    }

                    if (type == "WHERE")
                    {
                        if (updWhere == string.Empty)
                        {
                            updWhere = "WHERE ";
                        }
                        else
                        {
                            updWhere += " AND ";
                        }

                        updWhere += "[" + dbcol + "] =" + value;

                    }
                    else if (updValues == string.Empty)
                    {
                        updValues = "SET " + "[" + dbcol + "] =" + value;
                    }
                    else
                    {
                        updValues += "," + "[" + dbcol + "] =" + value;
                    }
                }
                string commandText = updHeader + " " + updValues + " " + updWhere + ";";
                using (SqlCommand command = new SqlCommand(commandText, connection))
                {
                    command.Transaction = trans;
                    int res = command.ExecuteNonQuery();
                    if (res <= 0)
                    {
                        throw new Exception("SQL Command was no affected (" + commandText + ")");
                    }
                }

            }
        }

        public static void ZipStr(string str, string path)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (DeflateStream gzip =
                  new DeflateStream(output, CompressionMode.Compress))
                {
                    using (StreamWriter writer =
                      new StreamWriter(gzip, System.Text.Encoding.UTF8))
                    {
                        writer.Write(str);
                    }
                }

                File.WriteAllBytes(path, output.ToArray());

            }
        }

    }

    public class Input
    {
        public string base64 { get; set; }
        public string mec { get; set; }

    }
}
