using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;


namespace BtcChinaBot.web
{
    public partial class Configurations : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            NameValueCollection section = (NameValueCollection)ConfigurationManager.GetSection("BotConfigurations");
            foreach (var key in section.AllKeys)
                theLiteral.Text += key + ": " + section[key] + "<br/>";
        }
    }
}