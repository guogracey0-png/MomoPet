using System.Collections.Generic;
namespace MomoPetApp
{
    public class LocalOcrBlock { public string Text; public double X,Y,Width,Height; }
    public class LocalOcrOutput { public string Text; public List<LocalOcrBlock> Blocks=new List<LocalOcrBlock>(); }
}
