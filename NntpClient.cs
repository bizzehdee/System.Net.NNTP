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
    internal TcpClient m_client;

    public delegate void OnDataRecievedCB(object sender, EventArgs e);
    public event OnDataRecievedCB OnDataRecieved;
    
    /// <summary>
    /// Constructor, creates the tcpip client
    /// </summary>
    public NntpClient() 
    {
      m_client = new TcpClient();
    }

    /// <summary>
    /// Connect to the NNTP server
    /// </summary>
    /// <param name="server">server domain name, e.g. news.virginmedia.com</param>
    /// <exception cref="NntpException"></exception>
    public void Connect(String server)
    {
      String m_response = "";

      m_client.Connect(server, 119);

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "200")
      {
        throw new NntpException(m_response);
      }

      //GetHelp();
    }

    /// <summary>
    /// Sends the quit command and then closes the connection
    /// </summary>
    public void Disconnect()
    {
      String m_response = "";

      this.Write("QUIT");

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "205")
      {
        throw new NntpException(m_response);
      }

      m_client.Close();
    }

    /// <summary>
    /// Use to send AUTHINFO USER for nntp servers that require login
    /// </summary>
    /// <param name="user">plain text username</param>
    public void SendAuthinfoUser(String user)
    {
      String m_response = "";

      this.Write("AUTHINFO USER " + user);

      m_response = this.Response();

      if (m_response.Substring(0, 3) == "482")
      {
        throw new NntpException(m_response);
      }

      if (m_response.Substring(0, 3) == "502")
      {
        throw new NntpException(m_response);
      }
    }

    /// <summary>
    /// used to send AUTHINFO PASS for nntp servers that require login
    /// </summary>
    /// <param name="pass">plain text password</param>
    public void SendAuthinfoPass(String pass)
    {
      String m_response = "";

      this.Write("AUTHINFO PASS " + pass);

      m_response = this.Response();

      if (m_response.Substring(0, 3) == "482")
      {
        throw new NntpException(m_response);
      }

      if (m_response.Substring(0, 3) == "502")
      {
        throw new NntpException(m_response);
      }
    }

    /// <summary>
    /// get the list of all the available groups
    /// </summary>
    /// <returns>NntpGroupList</returns>
    public NntpGroupList GetGroupList()
    {
      return GetGroupList(""); //match everything
    }

    /// <summary>
    /// Get a list of groups that match the match string (regex)
    /// </summary>
    /// <param name="match">regex match string to test the group name against</param>
    /// <returns>NntpGroupList</returns>
    public NntpGroupList GetGroupList(String match)
    {
      String m_response = "";
      NntpGroupList m_list = new NntpGroupList();

      this.Write("LIST");

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "215")
      {
        throw new NntpException(m_response);
      }

      while (true)
      {
        m_response = this.Response();

        if (m_response == ".\r\n" || m_response == ".\n")
        {
          return m_list;
        }
        else
        {
          char[] m_seperator = { ' ' };
          String[] m_values = m_response.Split(m_seperator);
          if (match == "") /*ignore expensive regex if we dont need to use it */
          {
            m_list.Add(m_values[0]);
          }
          else
          {
            Match m_match = Regex.Match(m_values[0], match, RegexOptions.ECMAScript | RegexOptions.IgnoreCase);

            if (m_match.Success)
            {
              m_list.Add(m_values[0]);
            }
          }
          continue;
        }
      }
    }

    /// <summary>
    /// Selects the newsgroup to set as current
    /// </summary>
    /// <param name="group">newsgroup name. e.g. alt.bin.misc</param>
    public void SelectGroup(String group)
    {
      String m_response = "";

      this.Write("GROUP " + group);

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "211")
      {
        throw new NntpException(m_response);
      }
    }

    /// <summary>
    /// Gets the entire article where "article" is the message id
    /// </summary>
    /// <param name="article">message id</param>
    /// <returns>NntpArticle with full artical including headers</returns>
    public NntpArticle GetArticle(String article)
    {
      if (article == null) throw new NntpException("article == null");
      if (article == "") throw new NntpException("article == \"\"");

      NntpArticle m_article = new NntpArticle();

      m_article.ID = article;
      m_article.Headers = this.GetArticleHeaders(article);
      m_article.Body = this.GetArticleBody(article);

      return m_article;
    }

    /// <summary>
    /// Gets all headers for the specified article
    /// </summary>
    /// <param name="article">message id</param>
    /// <returns>NntpHeaderList all headers</returns>
    /// <exception cref="NntpException"></exception>
    public NntpHeaderList GetArticleHeaders(String article)
    {
      if (article == null) throw new NntpException("article == null");
      if (article == "") throw new NntpException("article == \"\"");

      String m_response = "";
      NntpHeaderList m_headers = new NntpHeaderList();

      this.Write("HEAD <" + article + ">");

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "221")
      {
        throw new NntpException(m_response);
      }

      //consider adding in a progress event in here somewhere
      while (true)
      {
        m_response = this.Response();

        if (m_response == ".\r\n" || m_response == ".\n")
        {
          break;
        }

        char[] m_seperator = { ':' };
        String[] m_parts = m_response.Split(m_seperator, 2);

        m_headers.Add(new NntpHeader(m_parts[0].Trim(), m_parts[1].Trim()));
      }

      return m_headers;
    }

    /// <summary>
    /// Gets the raw body for the specified article
    /// </summary>
    /// <param name="article">message id</param>
    /// <returns>String containing body of article</returns>
    public String GetArticleBody(String article)
    {
      if (article == null) throw new NntpException("article == null");
      if (article == "") throw new NntpException("article == \"\"");

      String m_response = "";
      String m_article = "";
      StringBuilder m_article_builder = new StringBuilder();

      this.Write("BODY <" + article + ">");

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "222")
      {
        throw new NntpException(m_response);
      }

      UInt32 i = 0;
      while (true)
      {
        m_response = this.Response();

        if (OnDataRecieved != null)
        {
          byte[] m_bytes = ASCIIEncoding.ASCII.GetBytes(m_response);
          OnDataRecieved(this, new NntpEventArgs(m_bytes, i++));
        }

        if (m_response == ".\r\n" || m_response == ".\n")
        {
          break;
        }

        m_article_builder.Append(m_response);
      }

      m_article = m_article_builder.ToString();

      return m_article;
    }

    #region Internals

    internal void Write(String str)
    {
      ASCIIEncoding m_enc = new ASCIIEncoding();
      byte[] m_buf = new byte[1024];
      NetworkStream m_stream = m_client.GetStream();

      if (!str.EndsWith("\r\n")) str += "\r\n";

      m_buf = m_enc.GetBytes(str);

      m_stream.Write(m_buf, 0, m_buf.Length);
    }

    internal string Response()
    {
      String m_ret = "";
      ASCIIEncoding m_enc = new ASCIIEncoding();
      byte[] m_buf = new byte[1024];
      int m_count = 0;
      NetworkStream m_stream = m_client.GetStream();

      while (true)
      {
        byte[] m_rbuf = new byte[2];
        int m_bytes = m_stream.Read(m_rbuf, 0, 1);
        if (m_bytes == 1)
        {
          m_buf[m_count] = m_rbuf[0];
          m_count++;

          if (m_rbuf[0] == '\n')
          {
            break;
          }
        }
        else
        {
          break;
        }
      }

      m_ret = m_enc.GetString(m_buf, 0, m_count);

      return m_ret;
    }

    internal void GetHelp()
    {
      String m_response = "";

      this.Write("HELP");

      m_response = this.Response();

      if (m_response.Substring(0, 3) != "100")
      {
        throw new NntpException(m_response);
      }

      while (true)
      {
        m_response = this.Response();
        m_response = m_response.TrimStart();

        if (m_response == ".\r\n" || m_response == ".\n")
        {
          break;
        }
        else
        {
          char[] m_seperator = { ' ' };
          String[] m_values = m_response.Split(m_seperator);

          //ValidCommands.Add(m_values[0].TrimEnd());

          continue;
        }
      }
    }

    #endregion

  }
}
