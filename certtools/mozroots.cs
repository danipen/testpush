//
// mozroots.cs: Import the Mozilla's trusted root certificates into Mono
//
// Authors:
//  Sebastien Pouliot <sebastien@ximian.com>
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using Mono.Security.Authenticode;
using Mono.Security.X509;

namespace Mono.Tools {

    class MozRoots {

        private const string defaultUrl =
            "http://mxr.mozilla.org/seamonkey/source/security/nss/lib/ckfw/builtins/certdata.txt?raw=1";

        static string url;
        static string inputFile;
        static string pkcs7filename;
        static bool import;
        static bool machine;
        static bool confirmAddition;
        static bool confirmRemoval;
        static bool quiet;
        static bool refuseRemoval;

        static byte[] DecodeOctalString (string s)
        {
            string[] pieces = s.Split ('\\');
            byte[] data = new byte[pieces.Length - 1];
            for (int i = 1; i < pieces.Length; i++)
            {
                int firstDigit = pieces[i][0] - '0' << 6;
                int secondDigit = pieces[i][1] - '0' << 3;
                int thirdDigit = pieces[i][2] - '0';
                data[i - 1] = (byte)(firstDigit + secondDigit + thirdDigit);
            }
            return data;
        }

        static X509Certificate DecodeCertificate(string s)
        {
            byte[] rawdata = DecodeOctalString(s);
            return new X509Certificate(rawdata);
        }

        static Stream GetFile()
        {
            try
            {
                if (inputFile != null)
                    return File.OpenRead(inputFile);
                WriteLine("Downloading from '{0}'...", url);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 10000;
                return req.GetResponse().GetResponseStream();
            }
            catch
            {
                return null;
            }
        }

        static X509CertificateCollection DecodeCollection()
        {
            X509CertificateCollection roots = new X509CertificateCollection ();
            StringBuilder sb = new StringBuilder ();
            bool processing = false;

            using (Stream s = GetFile())
            {
                if (s == null)
                {
                    WriteLine("Couldn't retrieve the file using the supplied information.");
                    return null;
                }

                StreamReader sr = new StreamReader(s);
                while (true)
                {
                    string line = sr.ReadLine();
                    if (line == null)
                        break;

                    if (processing)
                    {
                        if (line.StartsWith ("END"))
                        {
                            processing = false;
                            X509Certificate root = DecodeCertificate(sb.ToString());
                            roots.Add(root);

                            sb = new StringBuilder();
                            continue;
                        }
                        sb.Append(line);
                    }
                    else
                    {
                        processing = line.StartsWith("CKA_VALUE MULTILINE_OCTAL");
                    }
                }
                return roots;
            }
        }

        static int Process ()
        {
            X509CertificateCollection roots = DecodeCollection ();
            if (roots == null)
                return 1;

            if (roots.Count == 0)
            {
                WriteLine ("No certificates were found.");
                return 0;
            }

            if (pkcs7filename != null)
                SaveCertificatesToFile(roots, pkcs7filename);

            if (!import)
                return 0;

            int addedCerts = AddDecodedCertificatesToStore(roots, GetStores());
            if (addedCerts > 0)
            {
                WriteLine (
                    "{0} new root certificates were added to your trust store.",
                    addedCerts);
            }

            RemoveCertificatesIfNotInStore(roots, GetStores());

            WriteLine ("Import process completed.{0}", Environment.NewLine);
            return 0;
        }

        static void SaveCertificatesToFile(
            X509CertificateCollection certificates, string filename)
        {
            SoftwarePublisherCertificate pkcs7 = new SoftwarePublisherCertificate();
            pkcs7.Certificates.AddRange(certificates);

            WriteLine ("Saving root certificates into '{0}' file...", filename);
            using (FileStream fs = File.OpenWrite(filename))
            {
                byte[] data = pkcs7.GetBytes ();
                fs.Write (data, 0, data.Length);
                fs.Close ();
            }
        }

        static int AddDecodedCertificatesToStore(
            X509CertificateCollection certs, X509Stores stores)
        {
            WriteLine("Importing certificates into {0} store...",
                machine ? "machine" : "user");

            X509CertificateCollection trusted = stores.TrustedRoot.Certificates;
            int additions = 0;

            foreach (X509Certificate cert in certs)
            {
                if (trusted.Contains(cert))
                    continue;
                if (confirmAddition && !AskConfirmation("add", cert))
                    continue;

                stores.TrustedRoot.Import(cert);
                if (confirmAddition)
                    WriteLine("Certificate added.{0}", Environment.NewLine);
                additions++;
            }
            return additions;
        }

        static X509Stores GetStores()
        {
            if (machine)
                return X509StoreManager.LocalMachine;
            return X509StoreManager.CurrentUser;
        }

        static void RemoveCertificatesIfNotInStore(
            X509CertificateCollection certs, X509Stores stores)
        {
            if (refuseRemoval)
                return;

            X509CertificateCollection trusted = stores.TrustedRoot.Certificates;
            X509CertificateCollection removed = new X509CertificateCollection();
            foreach (X509Certificate trust in trusted)
            {
                if (!certs.Contains(trust))
                    removed.Add(trust);
            }

            if (removed.Count == 0)
                return;

            WriteLine(GetRemovalMessage(), removed.Count);

            foreach (X509Certificate old in removed) {
                if (confirmRemoval && !AskConfirmation ("remove", old))
                    continue;

                stores.TrustedRoot.Remove(old);
                if (confirmRemoval)
                    WriteLine("Certificate removed.{0}", Environment.NewLine);
            }
        }

        static string GetRemovalMessage()
        {
            if (confirmRemoval)
                return "{0} previously trusted certificates were not part of the update.";
            return "{0} previously trusted certificates were removed.";
        }

        static string Thumbprint (string algorithm, X509Certificate certificate)
        {
            HashAlgorithm hash = HashAlgorithm.Create (algorithm);
            byte[] digest = hash.ComputeHash (certificate.RawData);
            return BitConverter.ToString (digest);
        }

        static bool AskConfirmation (string action, X509Certificate certificate)
        {
            // the quiet flag is ignored for confirmations
            Console.WriteLine ();
            Console.WriteLine ("Issuer: {0}", certificate.IssuerName);
            Console.WriteLine ("Serial number: {0}", BitConverter.ToString (certificate.SerialNumber));
            Console.WriteLine ("Valid from {0} to {1}", certificate.ValidFrom, certificate.ValidUntil);
            Console.WriteLine ("Thumbprint SHA-1: {0}", Thumbprint ("SHA1", certificate));
            Console.WriteLine ("Thumbprint MD5:   {0}", Thumbprint ("MD5", certificate));
            while (true) {
                Console.Write ("Are you sure you want to {0} this certificate ? ", action);
                string s = Console.ReadLine ().ToLower ();
                if (s == "yes")
                    return true;
                else if (s == "no")
                    return false;
            }
        }

        static bool ParseOptions (string[] args)
        {
            if (args.Length < 1)
                return false;

            // set defaults
            url = defaultUrl;
            confirmAddition = true;
            confirmRemoval = true;
            refuseRemoval = false;

            for (int i = 0; i < args.Length; i++) {
                switch (args[i]) {
                case "--url":
                    if (i >= args.Length - 1)
                        return false;
                    url = args[++i];
                    break;
                case "--file":
                    if (i >= args.Length - 1)
                        return false;
                    inputFile = args[++i];
                    break;
                case "--pkcs7":
                    if (i >= args.Length - 1)
                        return false;
                    pkcs7filename = args[++i];
                    break;
                case "--import":
                    import = true;
                    break;
                case "--machine":
                    machine = true;
                    break;
                case "--sync":
                    confirmAddition = false;
                    confirmRemoval = false;
                    break;
                case "--ask":
                    confirmAddition = true;
                    confirmRemoval = true;
                    break;
                case "--ask-add":
                    confirmAddition = true;
                    confirmRemoval = false;
                    break;
                case "--ask-remove":
                    confirmAddition = false;
                    confirmRemoval = true;
                    break;
                case "--add-only":
                    confirmAddition = false;
                    refuseRemoval = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    WriteLine ("Unknown option '{0}'.");
                    return false;
                }
            }
            return true;
        }

        static void Header ()
        {
            Console.WriteLine(
                "Mozilla Roots Importer - version 3.12.0 (Codice Software Custom)");
            Console.WriteLine(
                "Download and import trusted root certificates from Mozilla's MXR.");
            Console.WriteLine(
                "Copyright 2002, 2003 Motus Technologies. Copyright 2004-2008 Novell. BSD licensed.");
            Console.WriteLine();
        }

        static void Help ()
        {
            Console.WriteLine ("Usage: mozroots [--import [--machine] [--sync | --ask | --ask-add | --ask-remove | --add-only]]");
            Console.WriteLine ("Where the basic options are:");
            Console.WriteLine (" --import\tImport the certificates into the trust store.");
            Console.WriteLine (" --sync\t\tSynchronize (add/remove) the trust store with the certificates.");
            Console.WriteLine (" --ask\t\tAlways confirm before adding or removing trusted certificates.");
            Console.WriteLine (" --ask-add\tAlways confirm before adding a new trusted certificate.");
            Console.WriteLine (" --ask-remove\tAlways confirm before removing an existing trusted certificate.");
            Console.WriteLine (" --add-only\tAutomatically accept new certificates. Do not remove any trusted one.");
            Console.WriteLine ("{0}and the advanced options are", Environment.NewLine);
            Console.WriteLine (" --url url\tSpecify an alternative URL for downloading the trusted");
            Console.WriteLine ("\t\tcertificates (MXR source format).");
            Console.WriteLine (" --file name\tDo not download but use the specified file.");
            Console.WriteLine (" --pkcs7 name\tExport the certificates into a PKCS#7 file.");
            Console.WriteLine (" --machine\tImport the certificate in the machine trust store.");
            Console.WriteLine ("\t\tThe default is to import into the user store.");
            Console.WriteLine (" --quiet\tLimit console output to errors and confirmations messages.");
        }

        static void WriteLine(string str)
        {
            if (!quiet)
                Console.WriteLine (str);
        }

        static void WriteLine(string format, params object[] args)
        {
            if (!quiet)
                Console.WriteLine (format, args);
        }

        static int Main(string[] args)
        {
            try
            {
                if (!ParseOptions (args))
                {
                    Header ();
                    Help ();
                    return 1;
                }
                if (!quiet)
                {
                    Header ();
                }
                return Process ();
            }
            catch (Exception e)
            {
                // ignore quiet on exception
                Console.WriteLine ("Error: {0}", e);
                return 1;
            }
        }
    }
}
