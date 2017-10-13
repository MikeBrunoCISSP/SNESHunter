using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;
using System.Net.Mail;

namespace snesclassicalert
{
    public class EmailHandler
    {
        private static string rfc_2822_pattern = @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z";
        const string CONF_FILE = @".\email.conf";

        const char DELIM = '=';

        const string FROM_EMAIL_HEADER = "from_address";
        const string FROM_EMAIL_NAME_HEADER = "from_name";
        const string TO_EMAIL_NAME_HEADER = "to_name";
        const string TO_EMAIL_HEADER = "to_address";
        const string SERVER_HEADER = "smtp_server";
        const string PORT_HEADER = "smtp_port";
        const string USERNAME_HEADER = "user_name";
        const string PASSWORD_HEADER = "password";
        const string USE_SSL_HEADER = "use_ssl";


        private bool gotFromEmail,
                     gotToEmail,
                     gotFromName,
                     gotToName,
                     gotServer,
                     gotPort,
                     gotUserName,
                     gotPassword,
                     gotUseSSL;

        private  string userName,
                        password;

        private SmtpClient smtpClient;
        private MailMessage message;

        public bool usable;

        public EmailHandler()
        {
            if (File.Exists(CONF_FILE))
            {
                if (loadSettingsFromFile())
                {
                    usable = true;
                    return;
                }
                else
                    File.Delete(CONF_FILE);
            }

            string sFromEmail,
                   sToEmail,
                   sFromName,
                   sToName,
                   smtp_server;

            int smtp_port = 0;

            bool enableSSL = false;

            MailAddress from_address,
                        to_address;

            using (FileStream fs = new FileStream(CONF_FILE, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                using (StreamWriter stream = new StreamWriter(fs))
                {
                    //Get Email FROM address
                    sFromEmail = getEmail("Enter Email FROM address");
                    stream.WriteLine(FROM_EMAIL_HEADER + DELIM + sFromEmail);
                    gotFromEmail = true;

                    //Get Email TO address
                    sToEmail = getEmail("Enter Email TO address");
                    stream.WriteLine(TO_EMAIL_HEADER + DELIM + sToEmail);
                    gotToEmail = true;

                    //Get Email FROM name
                    Console.Write("Enter Email FROM name: ");
                    sFromName = Console.ReadLine();
                    stream.WriteLine(FROM_EMAIL_NAME_HEADER + DELIM + sFromName);
                    gotFromName = true;

                    //Get Email TO name
                    Console.Write("Enter Email TO name: ");
                    sToName = Console.ReadLine();
                    stream.WriteLine(TO_EMAIL_NAME_HEADER + DELIM + sToName);
                    gotToName = true;

                    //Get SMTP server
                    bool validSMTPServer = false;
                    do
                    {
                        Console.Write("Enter mail server: ");
                        smtp_server = Console.ReadLine();
                        if (!isValidFQDN(smtp_server))
                            Console.WriteLine(smtp_server + ": not a valid FQDN.");
                        else
                            validSMTPServer = true;
                    }
                    while (!validSMTPServer);
                    stream.WriteLine(SERVER_HEADER + DELIM + smtp_server);
                    gotServer = true;

                    //Get SMTP port
                    string sPort;
                    bool isValidPort = false;

                    do
                    {
                        Console.Write("Enter mail server port number: ");
                        sPort = Console.ReadLine();
                        if (!Regex.IsMatch(sPort, "^[0-9]+$"))
                            Console.WriteLine(sPort + ": not a valid port number.");
                        else
                        {
                            smtp_port = Convert.ToInt16(sPort);
                            isValidPort = checkPort(smtp_port);
                        }
                    }
                    while (!isValidPort);
                    stream.WriteLine(PORT_HEADER + DELIM + sPort);
                    gotPort = true;

                    //Get Username
                    Console.Write("Enter Email account username (just hit ENTER if its the same as your Email FROM address): ");
                    userName = Console.ReadLine();
                    if (userName == string.Empty)
                        userName = sFromEmail;
                    stream.WriteLine(USERNAME_HEADER + DELIM + userName);
                    gotUserName = true;

                    //Get Password
                    Console.Write("Enter Email account password: ");
                    password = Crypto.EncryptStringAES(getPasswordMasked());
                    stream.WriteLine(PASSWORD_HEADER + DELIM + password);
                    gotPassword = true;

                    //Check for SSL
                    bool validResponse = false;
                    do
                    {
                        Console.Write("\r\nDo you want to enable SSL encryption for Email notifications? (Y/N): ");
                        char response = Console.ReadKey(true).KeyChar;
                        Console.Write("\r\n\r\n");

                        switch (response)
                        {
                            case 'y':
                                enableSSL = true;
                                validResponse = true;
                                break;
                            case 'Y':
                                enableSSL = true;
                                validResponse = true;
                                break;
                            case 'n':
                                enableSSL = false;
                                validResponse = true;
                                break;
                            case 'N':
                                enableSSL = false;
                                validResponse = true;
                                break;
                            default:
                                Console.WriteLine(response + ": Not a valid response.");
                                validResponse = false;
                                break;
                        }
                    }
                    while (!validResponse);
                    stream.WriteLine(USE_SSL_HEADER + DELIM + (enableSSL ? "TRUE" : "FALSE"));
                    gotUseSSL = true;
                }

                try
                {
                    from_address = new MailAddress(sFromEmail);
                    to_address = new MailAddress(sToEmail, sToName);

                    smtpClient = new SmtpClient(smtp_server, smtp_port);
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtpClient.EnableSsl = enableSSL;

                    message = new MailMessage(from_address, to_address);

                    bool validResponse = false;
                    string response;
                    do
                    {
                        Console.Write("Would you like to send a test message to ensure alerting is working? (Y/N): ");
                        response = Console.ReadKey(true).KeyChar.ToString().ToUpper();
                        switch (response)
                        {
                            case "Y":
                                if (sendTestEmail())
                                    usable = true;
                                else
                                    File.Delete(CONF_FILE);
                                validResponse = true;
                                break;
                            case "N":
                                usable = true;
                                validResponse = true;
                                break;
                            default:
                                Console.WriteLine("Please respond with \"Y\" or \"N\".");
                                break;
                        }
                    }
                    while (!validResponse);
                    usable = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unable to initialize the Email handler:\r\n" + ex.ToString());
                    File.Delete(CONF_FILE);
                    usable = false;
                }
            }
        }

        private bool loadSettingsFromFile()
        {
            string record;

            string fromName = string.Empty;
            string fromEmail = string.Empty;
            string toName = string.Empty;
            string toEmail = string.Empty;
            string server = string.Empty;

            MailAddress from_address,
                        to_address;

            int port = -1;

            bool useSSL = false;

            string[] parts;

            string error = "Error in configuration file: ";

            using (FileStream fs = new FileStream(CONF_FILE, FileMode.Open, FileAccess.Read))
            {
                using (StreamReader stream = new StreamReader(fs))
                {
                    while (stream.Peek() != -1)
                    {
                        record = stream.ReadLine();
                        parts = record.Split(DELIM);

                        switch (parts[0])
                        {
                            case FROM_EMAIL_HEADER:
                                if (Regex.IsMatch(parts[1], rfc_2822_pattern, RegexOptions.IgnoreCase))
                                {
                                    fromEmail = parts[1];
                                    gotFromEmail = true;
                                }
                                else
                                    Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid Email address.");
                                break;

                            case FROM_EMAIL_NAME_HEADER:
                                fromName = parts[1];
                                gotFromName = true;
                                break;

                            case TO_EMAIL_HEADER:
                                if (Regex.IsMatch(parts[1], rfc_2822_pattern, RegexOptions.IgnoreCase))
                                {
                                    toEmail = parts[1];
                                    gotToEmail = true;
                                }
                                else
                                    Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid Email address.");
                                break;

                            case TO_EMAIL_NAME_HEADER:
                                toName = parts[1];
                                gotToName = true;
                                break;

                            case SERVER_HEADER:
                                if (isValidFQDN(parts[1]))
                                {
                                    server = parts[1];
                                    gotServer = true;
                                }
                                else
                                    Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid server name.");
                                break;

                            case PORT_HEADER:
                                if (Regex.IsMatch(parts[1], "^[0-9]+$"))
                                {
                                    port = Convert.ToInt16(parts[1]);
                                    if (checkPort(port))
                                        gotPort = true;
                                    else
                                        Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid port number.");
                                }
                                else
                                    Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid port number.");
                                break;

                            case USERNAME_HEADER:
                                userName = parts[1];
                                gotUserName = true;
                                break;

                            case PASSWORD_HEADER:
                                try
                                {
                                    password = string.Empty;
                                    for (int x = 1; x < parts.Length; x++)
                                    {
                                        if (parts[x] == string.Empty)
                                            password = password + DELIM;
                                        else
                                            password = password + parts[x];
                                    }
                                    string plaintext = Crypto.DecryptStringAES(password);
                                    plaintext = RandomString();
                                    gotPassword = true;
                                    break;
                                }
                                catch (Exception)
                                {
                                    Console.WriteLine(error + "password cryptogram is not readable");
                                    break;
                                }

                            case USE_SSL_HEADER:
                                switch (parts[1].ToUpper())
                                {
                                    case "TRUE":
                                        useSSL = true;
                                        gotUseSSL = true;
                                        break;
                                    case "T":
                                        useSSL = true;
                                        gotUseSSL = true;
                                        break;
                                    case "YES":
                                        useSSL = true;
                                        gotUseSSL = true;
                                        break;
                                    case "Y":
                                        useSSL = true;
                                        gotUseSSL = true;
                                        break;
                                    case "FALSE":
                                        useSSL = false;
                                        gotUseSSL = true;
                                        break;
                                    case "F":
                                        useSSL = false;
                                        gotUseSSL = true;
                                        break;
                                    case "NO":
                                        useSSL = false;
                                        gotUseSSL = true;
                                        break;
                                    case "N":
                                        useSSL = false;
                                        gotUseSSL = true;
                                        break;
                                    default:
                                        Console.WriteLine(error + "\"" + parts[1] + "\" is not a valid setting for use_ssl. Please use Yes or No");
                                        break;
                                }
                                break;
                            default:
                                Console.WriteLine(error + "Unreadable line: \"" + record + "\"");
                                break;
                        }
                    }
                }
            }

            if (allDefined())
            {
                try
                {
                    from_address = new MailAddress(fromEmail, fromName);
                    to_address = new MailAddress(toEmail, toName);

                    smtpClient = new SmtpClient(server, port);
                    smtpClient.UseDefaultCredentials = false;
                    smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtpClient.EnableSsl = useSSL;

                    message = new MailMessage(from_address, to_address);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Unable to initialize the Email handler:\r\n" + ex.ToString());
                    return false;
                }
            }
            else
            {
                Console.WriteLine(error + "Not all settings defined.");
                return false;
            }
        }

        public void sendAlert(string merchantName, bool available)
        {
            try
            {
                message.Subject = merchantName + ": " + (available ? "SNES Classic in stock!!" : "SNES Classic out of stock...");
                message.Body = message.Subject;
                smtpClient.Credentials = new System.Net.NetworkCredential(userName,
                                                                    Crypto.DecryptStringAES(password));
                smtpClient.Send(message);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception occurred when attempting to send an alert message:\r\n" + ex.ToString());
            }
        }

        public bool sendTestEmail()
        {
            try
            {
                message.Subject = "SNES Hunter - Test Email";
                message.Body = message.Subject;
                smtpClient.Credentials = new System.Net.NetworkCredential(userName,
                                                                    Crypto.DecryptStringAES(password));
                smtpClient.Send(message);

                bool validResponse = false;
                bool result = false;
                string response;
                do
                {
                    Console.Write("Did you receive the test Email? (Y/N): ");
                    response = Console.ReadKey(true).KeyChar.ToString().ToUpper();
                    switch (response)
                    {
                        case "Y":
                            result = true;
                            validResponse = true;
                            break;
                        case "N":
                            Console.WriteLine("Please confirm your Email settings and try again.");
                            result = false;
                            validResponse = true;
                            break;
                        default:
                            Console.WriteLine("Please respond with \"Y\" or \"N\".");
                            break;
                    }
                }
                while (!validResponse);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception occurred when attempting to send the test message:\r\n" + ex.ToString());
                return false;
            }

        }

        private bool allDefined()
        {
            return gotFromEmail & gotFromName & gotToEmail & gotToName & gotServer & gotPort & gotUserName & gotPassword & gotUseSSL;
        }

        private static string getEmail(string prompt)
        {
            bool validEmail = false;
            string emailAddress;

            do
            {
                Console.Write(prompt + ": ");
                emailAddress = Console.ReadLine();
                if (Regex.IsMatch(emailAddress, rfc_2822_pattern, RegexOptions.IgnoreCase))
                    validEmail = true;
                else
                    Console.WriteLine(emailAddress + ": not a valid Email address.");
            }
            while (!validEmail);

            return emailAddress;
        }

        private static bool isValidFQDN(string pattern)
        {
            UriHostNameType result = Uri.CheckHostName(pattern);
            return (result != UriHostNameType.Unknown);
        }

        private static bool checkPort(int portNumber)
        {
            if (portNumber < 0 | portNumber > 65535)
            {
                Console.WriteLine(portNumber + ": not a valid port number.");
                return false;
            }
            else
                return true;
        }

        public static string getPasswordMasked()
        {
            ConsoleKeyInfo key;
            string pass = string.Empty;

            do
            {
                key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    pass += key.KeyChar;
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
                    {
                        pass = pass.Substring(0, (pass.Length - 1));
                        Console.Write("\b \b");
                    }
                }
            }
            while (key.Key != ConsoleKey.Enter);

            return pass;
        }

        public static string RandomString()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, 256)
              .Select(s => s[(new Random()).Next(s.Length)]).ToArray());
        }
    }
}
