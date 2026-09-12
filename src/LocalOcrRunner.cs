using System;
using System.Globalization;
using System.Text;
namespace MomoPetApp
{
    public static class LocalOcrRunner
    {
        static string Pack(string value){return Convert.ToBase64String(Encoding.UTF8.GetBytes(value??""));}
        public static void Main(string[] args)
        {
            try
            {
                if(args.Length==0)throw new ArgumentException("缺少图片路径");LocalOcrOutput result=LocalOcr.RecognizeAsync(args[0]).GetAwaiter().GetResult();Console.OutputEncoding=Encoding.UTF8;Console.WriteLine("TEXT\t"+Pack(result.Text));
                foreach(LocalOcrBlock block in result.Blocks)Console.WriteLine("BLOCK\t"+Pack(block.Text)+"\t"+block.X.ToString("R",CultureInfo.InvariantCulture)+"\t"+block.Y.ToString("R",CultureInfo.InvariantCulture)+"\t"+block.Width.ToString("R",CultureInfo.InvariantCulture)+"\t"+block.Height.ToString("R",CultureInfo.InvariantCulture));
            }
            catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.Exit(1);}
        }
    }
}
