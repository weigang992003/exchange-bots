using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using Common.Web;


namespace BtcChinaBot.web
{
    public partial class Default : Page
    {
        private static WebLogger _logger = new WebLogger(50);
        private static Thread _thread;


        protected void Page_Load(object sender, EventArgs e)
        {
            var item = Request.QueryString["item"];
            if (String.IsNullOrEmpty(item))
                _logger.Error("Item not specified");

            theLiteral.Text = _logger.ToHtmlString();
        }

        private void doAction()
        {
            while (true)
            {
                var now = DateTime.Now;
                var msg = "Now it's " + now.ToLongTimeString();
                if (now.Second % 3 == 0)
                    _logger.Message(msg);
                else if (now.Second % 3 == 1)
                    _logger.Warning(msg);
                else _logger.Error(msg);

                Thread.Sleep(1200);
            }
        }

        protected void btnStart_OnClick(object sender, EventArgs e)
        {
            _thread = new Thread(doAction);
             _thread.Start();
            _logger.Clear();
        }
    }
}