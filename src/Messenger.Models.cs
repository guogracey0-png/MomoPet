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
    public class MomoRemoteMember
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Nickname { get; set; }
        public string Avatar { get; set; }
        public string Bio { get; set; }
        public string SkinId { get; set; }
        public override string ToString(){return (String.IsNullOrWhiteSpace(Avatar)?"🐾":Avatar)+"  "+Nickname+"  @"+Username;}
    }

    public class MomoAttachment
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }
        public long Size { get; set; }
    }

    class MomoPreparedUpload
    {
        public List<string> Paths=new List<string>();
        public List<string> TemporaryFiles=new List<string>();
    }

    public class MomoLetter
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string SenderNickname { get; set; }
        public string ReceiverNickname { get; set; }
        public string SenderSkinId { get; set; }
        public string Content { get; set; }
        public List<MomoAttachment> Attachments { get; set; }
        public string CreatedAt { get; set; }
        public string Status { get; set; }
        public string DeliveredAt { get; set; }
        public string ReadAt { get; set; }
    }

    public class MomoGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OwnerId { get; set; }
        public List<string> MemberIds { get; set; }
        public List<MomoRemoteMember> Members { get; set; }
        public string CreatedAt { get; set; }
        public override string ToString(){return "🐾  "+Name+"  · "+(Members==null?0:Members.Count)+" 人";}
    }

    public class MomoGroupMessage
    {
        public string Id { get; set; }
        public string GroupId { get; set; }
        public string SenderId { get; set; }
        public string SenderNickname { get; set; }
        public string SenderSkinId { get; set; }
        public string Content { get; set; }
        public List<MomoAttachment> Attachments { get; set; }
        public string CreatedAt { get; set; }
    }
}

