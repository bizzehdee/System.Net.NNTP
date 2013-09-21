/*
 * Copyright (c) 2011, Darren Horrocks
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without modification, 
 * are permitted provided that the following conditions are met:
 * 
 * Redistributions of source code must retain the above copyright notice, this list 
 * of conditions and the following disclaimer.
 * Redistributions in binary form must reproduce the above copyright notice, this 
 * list of conditions and the following disclaimer in the documentation and/or 
 * other materials provided with the distribution.
 * Neither the name of Darren Horrocks/www.bizzeh.com nor the names of its 
 * contributors may be used to endorse or promote products derived from this software 
 * without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY 
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES 
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT 
 * SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, 
 * INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, 
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF 
 * THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

/*
 * 
 * 
 * post
 * 
 * yenc decoding
 * if byte is = (escape) move to next byte and byte = byte-64
 * else byte = byte-42
 */

namespace System.Net.Nntp
{
    public class NntpClient
    {
        private readonly TcpClient _client;
        private Stream _clientStream;

        public delegate void DataRecievedHandler(byte[] data, UInt32 line);
        public delegate void DataRecievedCB(object sender, EventArgs e);
        public event DataRecievedCB DataRecieved;
        public delegate void ConnectionFailedCB(object sender, EventArgs e);
        public event ConnectionFailedCB ConnectionFailed;

        public Boolean UsingSSL { get; private set; }

        /// <summary>
        /// Constructor, creates the tcpip client
        /// </summary>
        public NntpClient()
        {
            _client = new TcpClient();
        }

        /// <summary>
        /// Connect to the NNTP server
        /// </summary>
        /// <param name="server">server domain name, e.g. news.virginmedia.com</param>
        /// <exception cref="NntpException"></exception>
        public void Connect(String server)
        {
            Connect(server, 119);
        }

        /// <summary>
        /// Connect to the NNTP server
        /// </summary>
        /// <param name="server">server domain name, e.g. news.virginmedia.com</param>
        /// <param name="port">port number to use, eg. 119</param>
        public void Connect(String server, Int32 port)
        {
            Connect(server, port, false);
        }

        /// <summary>
        /// Connect to the NNTP server
        /// </summary>
        /// <param name="server">server domain name, e.g. news.virginmedia.com</param>
        /// <param name="port">port number to use, eg. 119</param>
        /// <param name="useSSL">use ssl during connection</param>
        public void Connect(String server, Int32 port, bool useSSL)
        {
            _client.Connect(server, port);

            _clientStream = _client.GetStream();

            UsingSSL = useSSL;

            //do we want to use ssl?
            if (useSSL)
            {
                //if so, pass the stream through an ssl stream and authenticate
                SslStream sslStream = new SslStream(_clientStream, true);
                sslStream.AuthenticateAsClient(server);

                _clientStream = sslStream;
            }

            String response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode != "200")
            {
                NntpConnectionEventArgs eventArgs = new NntpConnectionEventArgs(Int32.Parse(responseCode));

                if(ConnectionFailed != null) ConnectionFailed(this, eventArgs);

                if (!eventArgs.SuppressException)
                {
                    throw new NntpException(response, 200, Int32.Parse(response.Substring(0, 3)));
                }
            }
        }

        /// <summary>
        /// Sends the quit command and then closes the connection
        /// </summary>
        public void Disconnect()
        {
            String response = "";

            Write("QUIT");

            response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode != "205")
            {
                throw new NntpException(response, 205, Int32.Parse(responseCode));
            }

            _client.Close();
        }

        /// <summary>
        /// Use to send AUTHINFO USER for nntp servers that require login
        /// </summary>
        /// <param name="user">plain text username</param>
        public void SendAuthinfoUser(String user)
        {
            String response = "";

            Write("AUTHINFO USER {0}", user);

            response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode == "482")
            {
                throw new NntpException(response);
            }

            if (responseCode == "502")
            {
                throw new NntpException(response);
            }
        }

        /// <summary>
        /// used to send AUTHINFO PASS for nntp servers that require login
        /// </summary>
        /// <param name="pass">plain text password</param>
        public void SendAuthinfoPass(String pass)
        {
            String response = "";

            Write("AUTHINFO PASS {0}", pass);

            response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode == "482")
            {
                throw new NntpException(response);
            }

            if (responseCode == "502")
            {
                throw new NntpException(response);
            }
        }

        /// <summary>
        /// get the list of all the available groups
        /// </summary>
        /// <returns>NntpGroupList</returns>
        public IEnumerable<String> GetGroupList()
        {
            return GetGroupList(""); //match everything
        }

        /// <summary>
        /// Get a list of groups that match the match string (regex)
        /// </summary>
        /// <param name="match">regex match string to test the group name against</param>
        /// <returns>NntpGroupList</returns>
        public IEnumerable<String> GetGroupList(String match)
        {
            String response = "";
            List<String> groupList = new List<String>();

            Write("LIST ACTIVE {0}", match);

            response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode != "215")
            {
                throw new NntpException(response, 215, Int32.Parse(responseCode));
            }

            while (true)
            {
                response = Response();

                if (response == ".\r\n" || response == ".\n")
                {
                    return groupList;
                }

                String[] values = response.Split(' ');

                groupList.Add(values[0]);
            }
        }

        /// <summary>
        /// Selects the newsgroup to set as current
        /// </summary>
        /// <param name="group">newsgroup name. e.g. alt.bin.misc</param>
        public void SelectGroup(String group)
        {
            Write("GROUP {0}", group);

            String response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode != "211")
            {
                throw new NntpException(response, 211, Int32.Parse(responseCode));
            }
        }

        /// <summary>
        /// Gets the entire article where "article" is the message id
        /// </summary>
        /// <param name="article">message id</param>
        /// <returns>NntpArticle with full artical including headers</returns>
        public NntpArticle GetArticle(String article)
        {
            if (String.IsNullOrEmpty(article)) throw new ArgumentNullException("article");

            return new NntpArticle
                {
                    ID = article,
                    Headers = GetArticleHeaders(article),
                    Body = GetArticleBody(article)
                };
        }

        /// <summary>
        /// Gets all headers for the specified article
        /// </summary>
        /// <param name="article">message id</param>
        /// <returns>NntpHeaderList all headers</returns>
        /// <exception cref="NntpException"></exception>
        public NntpHeaderList GetArticleHeaders(String article)
        {
            if (String.IsNullOrEmpty(article)) throw new ArgumentNullException("article");

            NntpHeaderList headers = new NntpHeaderList();

            Write(String.Format("HEAD <{0}>", article));

            String response = Response();

            String responseCode = response.Substring(0, 3);
            if (responseCode != "221")
            {
                throw new NntpException(response, 221, Int32.Parse(responseCode));
            }

            //consider adding in a progress event in here somewhere
            while (true)
            {
                response = Response();

                if (response == ".\r\n" || response == ".\n")
                {
                    break;
                }

                String[] parts = response.Split(new[] { ':' }, 2);

                headers.Add(parts[0].Trim(), parts[1].Trim());
            }

            return headers;
        }

        /// <summary>
        /// Gets the raw body for the specified article
        /// </summary>
        /// <param name="article">message id</param>
        /// <returns>String containing body of article</returns>
        public String GetArticleBody(String article)
        {
            return GetArticleBody(article, null);
        }

        /// <summary>
        /// Gets the raw body for the specified article
        /// </summary>
        /// <param name="article">message id</param>
        /// <param name="action"></param>
        /// <returns>String containing body of article</returns>
        public String GetArticleBody(String article, DataRecievedHandler action)
        {
            if (String.IsNullOrEmpty(article)) throw new ArgumentNullException("article");

            StringBuilder articleBuilder = new StringBuilder();

            Write("BODY <{0}>", article);

            String response = this.Response();

            if (response.Substring(0, 3) != "222")
            {
                throw new NntpException(response, 222, Int32.Parse(response.Substring(0, 3)));
            }

            UInt32 i = 0;
            while (true)
            {
                response = Response();

                if (DataRecieved != null || action != null)
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(response);

                    if (DataRecieved != null)
                    {
                        DataRecieved(this, new NntpEventArgs(bytes, i));
                    }

                    if (action != null)
                    {
                        action(bytes, i);
                    }

                    i++;
                }

                if (response == ".\r\n" || response == ".\n")
                {
                    break;
                }

                articleBuilder.Append(response);
            }

            String articleName = articleBuilder.ToString();

            return articleName;
        }

        #region Internals

        private void Write(String str)
        {
            Write(str, new object[] { });
        }

        private void Write(String str, params object[] args)
        {
            if (args == null) throw new ArgumentNullException("args");

            ASCIIEncoding enc = new ASCIIEncoding();

            String writeString = String.Format(str, args);

            if (!writeString.EndsWith("\r\n")) writeString += "\r\n";

            byte[] buffer = enc.GetBytes(writeString);

            _clientStream.Write(buffer, 0, buffer.Length);
        }

        private String Response()
        {
            ASCIIEncoding asciiEncoding = new ASCIIEncoding();
            byte[] buffer = new byte[1024];
            int count = 0;

            while (true)
            {
                byte[] responseBuffer = new byte[2];
                int responseBytes = _clientStream.Read(responseBuffer, 0, 1);
                if (responseBytes != 1)
                {
                    break;
                }

                buffer[count] = responseBuffer[0];
                count++;

                if (responseBuffer[0] == '\n')
                {
                    break;
                }
            }

            return asciiEncoding.GetString(buffer, 0, count);
        }
        #endregion

    }
}
