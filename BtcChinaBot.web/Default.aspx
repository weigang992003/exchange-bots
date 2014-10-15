<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="BtcChinaBot.web.Default" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
    <style type="text/css">
        #console {
            float: left;
            height: 92vh;
            width: 800px;
            background-color: black;
            color: white;
            padding: 2px;
            overflow-y: scroll;
            font-family: monospace;
        }
        #configPanel {
            float: left;
            width: 320px;
            border: 1px solid red;
        }
        .warning {
            color: yellow;
        }
        .error {
            color: red;
        }
    </style>
</head>
<body>
    <form id="form1" runat="server">
        BtcChinaBot
        <div>
            <div id="console">
                <asp:Literal runat="server" ID="theLiteral"/>
            </div>
            <div id="configPanel">
                Configurations here<br/>
                <asp:Button runat="server" ID="btnStart" OnClick="btnStart_OnClick" Text="Start"/>
            </div>
        </div>
    </form>
</body>
</html>
