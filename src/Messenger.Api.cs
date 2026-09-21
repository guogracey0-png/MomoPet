using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MomoPetApp
{
    public partial class PetController
    {

        void UiPost(Action action)
        {
            if(action==null)return;
            try{var dispatcher=app==null?null:app.Dispatcher;if(dispatcher==null||dispatcher.HasShutdownStarted||dispatcher.HasShutdownFinished)return;dispatcher.BeginInvoke(action);}catch{}
        }

        void MomoApi<T>(string method,string path,object body,bool authenticated,Action<T> success,Action<string> failure)
        {
            string server=MomoServer(),token=momoToken;Task.Factory.StartNew(delegate{
                try{
                    var request=(HttpWebRequest)WebRequest.Create(server+path);request.Method=method;request.Accept="application/json";request.ContentType="application/json; charset=utf-8";request.Timeout=15000;request.ReadWriteTimeout=15000;if(authenticated&&!String.IsNullOrWhiteSpace(token))request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;
                    if(body!=null){byte[] bytes=Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(body));request.ContentLength=bytes.Length;using(var output=request.GetRequestStream())output.Write(bytes,0,bytes.Length);}
                    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();T value=String.IsNullOrWhiteSpace(text)?default(T):new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<T>(text);UiPost(new Action(delegate{if(success!=null)success(value);}));}
                }catch(WebException web){string message="无法连接云端";try{using(var response=web.Response)using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){string text=reader.ReadToEnd();var error=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;if(error!=null&&error.ContainsKey("error"))message=Convert.ToString(error["error"]);}}catch{}UiPost(new Action(delegate{if(failure!=null)failure(message);}));}
                catch(Exception error){UiPost(new Action(delegate{if(failure!=null)failure(error.Message);}));}
            });
        }
}
}