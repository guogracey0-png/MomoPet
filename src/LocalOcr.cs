using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace MomoPetApp
{
    public static class LocalOcr
    {
        public static async Task<LocalOcrOutput> RecognizeAsync(string imagePath)
        {
            StorageFile file=await StorageFile.GetFileFromPathAsync(imagePath).AsTask();
            using(var stream=await file.OpenAsync(FileAccessMode.Read).AsTask())
            {
                BitmapDecoder decoder=await BitmapDecoder.CreateAsync(stream).AsTask();
                using(SoftwareBitmap bitmap=await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied).AsTask())
                {
                    OcrEngine engine=OcrEngine.TryCreateFromUserProfileLanguages();
                    if(engine==null)throw new InvalidOperationException("Windows 没有可用的本地 OCR 语言包");
                    OcrResult result=await engine.RecognizeAsync(bitmap).AsTask();
                    var output=new LocalOcrOutput();var textLines=new List<string>();double width=Math.Max(1,bitmap.PixelWidth),height=Math.Max(1,bitmap.PixelHeight);
                    foreach(OcrLine line in result.Lines)
                    {
                        var words=line.Words.ToList();if(words.Count==0)continue;textLines.Add(line.Text);
                        foreach(OcrWord word in words)
                        {
                            var bounds=word.BoundingRect;if(String.IsNullOrWhiteSpace(word.Text))continue;
                            output.Blocks.Add(new LocalOcrBlock{Text=word.Text,X=bounds.X/width,Y=bounds.Y/height,Width=bounds.Width/width,Height=bounds.Height/height});
                        }
                    }
                    output.Text=String.Join(Environment.NewLine,textLines);return output;
                }
            }
        }
    }
}
