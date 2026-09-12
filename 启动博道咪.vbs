Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
app = files.BuildPath(folder, "MomoPet.exe")
shell.CurrentDirectory = folder

' 更新开发期间，优先启动最新构建；旧的 MomoPet.exe 若正在运行则不强制关闭。
preferred = files.BuildPath(folder, "MomoPet.next-0909-142921.exe")
If files.FileExists(preferred) Then app = preferred

' 编译产物采用 MomoPet.next-时间.exe，入口始终选择最后生成的一份。
latestTime = #1/1/1900#
If files.FileExists(app) Then latestTime = files.GetFile(app).DateLastModified
For Each candidate In files.GetFolder(folder).Files
    lowerName = LCase(candidate.Name)
    If (Left(lowerName, Len("momopet.next-")) = "momopet.next-" Or lowerName = "momopet.next.exe") And LCase(files.GetExtensionName(candidate.Name)) = "exe" Then
        If candidate.DateLastModified > latestTime Then
            app = candidate.Path
            latestTime = candidate.DateLastModified
        End If
    End If
Next
If Not files.FileExists(app) Then
    MsgBox "没有找到可启动的 MomoPet.exe 或 MomoPet.next-*.exe。请先运行 build.ps1。", 16, "博道咪"
    WScript.Quit 1
End If
shell.Run """" & app & """", 0, False
