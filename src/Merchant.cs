using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using System.Net.Mail;
using System.Reflection;

namespace snesclassicalert
{
    public class Merchant
    {
        private static bool WRITE_HTML_TO_FILE;
        private static string HTML_DIR;

        public string name,
                      url,
                      searchPattern;

        public bool usable,
                    inStock;

        public static EmailHandler emailHandler;

        public Merchant(string record)
        {
            string[] elements = record.Split(',');
            if (elements.Length != 3)
            {
                usable = false;
                return;
            }

            name = elements[0];
            url = elements[1];
            searchPattern = elements[2].ToUpper();

            inStock = false;
            usable = true;
        }

        public void checkAvailability()
        {
            string httpResponse;
            string outFile = string.Empty;

            bool currentlyInStock = inStock;

            try
            {
                CookieAwareWebClient client = new CookieAwareWebClient();
                httpResponse = client.getHTML(url).ToUpper();

                if (WRITE_HTML_TO_FILE)
                {
                    try
                    {
                        outFile = Path.Combine(HTML_DIR + name + ".html");
                        if (File.Exists(outFile))
                            File.Delete(outFile);
                        using (FileStream fs = new FileStream(outFile, FileMode.CreateNew, FileAccess.ReadWrite))
                        {
                            using (StreamWriter sw = new StreamWriter(fs))
                                sw.Write(httpResponse);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Could not create " + outFile + "\r\nStack Trace:\r\n" + ex.ToString());
                    }
                }

                inStock = httpResponse.IndexOf(searchPattern) != -1;
                if (!currentlyInStock & inStock)
                    emailHandler.sendAlert(name, true);
                else
                {
                    if (currentlyInStock & !inStock)
                        emailHandler.sendAlert(name, false);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Error on merchant " + name + ":\r\n" + ex.ToString() + "\r\n\r\n");
            }
        }

        public static void configHTMLOut()
        {
            try
            {
                if (Properties.Settings.Default.Write_GET_Responses_To_Files)
                {
                    HTML_DIR = Properties.Settings.Default.HTML_Output_Directory;
                    if (!Directory.Exists(HTML_DIR))
                        Directory.CreateDirectory(HTML_DIR);
                    WRITE_HTML_TO_FILE = true;
                }
                else
                    WRITE_HTML_TO_FILE = false;
            }
            catch (Exception)
            {
                Console.WriteLine("HTML File Directory specified in " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + ".config does not exist and could not be created.\r\nHTML output will not be written to files.");
                WRITE_HTML_TO_FILE = false;
            }
        }
    }

    public class CookieAwareWebClient : WebClient
    {
        public CookieContainer CookieContainer { get; set; }
        public Uri Uri { get; set; }

        public CookieAwareWebClient()
            : this(new CookieContainer())
        {
        }

        public CookieAwareWebClient(CookieContainer cookies)
        {
            this.CookieContainer = cookies;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request is HttpWebRequest)
            {
                (request as HttpWebRequest).CookieContainer = this.CookieContainer;
            }
            HttpWebRequest httpRequest = (HttpWebRequest)request;
            httpRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            return httpRequest;
        }

        protected override WebResponse GetWebResponse(WebRequest request)
        {
            WebResponse response = base.GetWebResponse(request);
            String setCookieHeader = response.Headers[HttpResponseHeader.SetCookie];

            //do something if needed to parse out the cookie.
            if (setCookieHeader != null)
            {
                Cookie cookie = new Cookie(); //create cookie
                cookie.Domain = "www.amazon.com";
                cookie.Name = "csm-hit";
                this.CookieContainer.Add(cookie);
            }

            return response;
        }

        public string getHTML(string url)
        {
            MethodInfo method = typeof(WebHeaderCollection).GetMethod
                        ("AddWithoutValidate", BindingFlags.Instance | BindingFlags.NonPublic);

            Uri uri = new Uri(url);
            WebRequest req = GetWebRequest(uri);
            req.Method = "GET";
            string key = "user-agent";
            string val = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/61.0.3163.100 Safari/537.36";

            method.Invoke(req.Headers, new object[] { key, val });

            using (StreamReader reader = new StreamReader(GetWebResponse(req).GetResponseStream()))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
