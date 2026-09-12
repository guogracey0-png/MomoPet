Set shell = CreateObject("WScript.Shell")
Set files = CreateObject("Scripting.FileSystemObject")
folder = files.GetParentFolderName(WScript.ScriptFullName)
app = files.BuildPath(folder, "MomoPet.exe")
shell.CurrentDirectory = folder

' 编译时不会覆盖正在运行的主程序，新版本会使用 MomoPet.next-时间.exe。
' 启动入口始终选择目录里最新的主程序，避免用户误开旧版。
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
