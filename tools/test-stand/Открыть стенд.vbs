Set WshShell = CreateObject("WScript.Shell")
Set FileSystem = CreateObject("Scripting.FileSystemObject")
strDir = FileSystem.GetParentFolderName(WScript.ScriptFullName)

' Проверяем, доступен ли сервер
Dim http, serverRunning
serverRunning = False
Set http = CreateObject("Microsoft.XMLHTTP")
On Error Resume Next
http.Open "GET", "http://localhost:7100/", False
http.Send
If http.Status = 200 Then
    serverRunning = True
End If
On Error GoTo 0

If Not serverRunning Then
    WshShell.CurrentDirectory = strDir
    WshShell.Run "pythonw server.py", 0, False
    WScript.Sleep 4000
End If

WshShell.Run "http://localhost:7100", 1, False
