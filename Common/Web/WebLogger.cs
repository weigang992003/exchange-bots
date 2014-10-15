using System.Collections.Generic;
using System.Text;


namespace Common.Web
{
    public class WebLogger : ILogger
    {
        private ushort _capacity;
        private List<string> _messages;


        public WebLogger(ushort capacity)
        {
            _capacity = capacity;
            _messages = new List<string>(capacity);
        }


        public void Message(string text)
        {
            if (_messages.Count == _capacity)
                _messages.RemoveAt(0);

            _messages.Add("<span>" + text + "</span>");
        }

        public void Warning(string text)
        {
            if (_messages.Count == _capacity)
                _messages.RemoveAt(0);

            _messages.Add("<span class='warning'>" + text + "</span>");
        }

        public void Error(string text)
        {
            if (_messages.Count == _capacity)
                _messages.RemoveAt(0);

            _messages.Add("<span class='error'>" + text + "</span>");
        }

        public string ToHtmlString()
        {
            var html = new StringBuilder();
            foreach (var message in _messages)
                html.Append(message + "<br/>");

            return html.ToString();
        }

        public void Clear()
        {
            _messages.Clear();
        }
    }
}
