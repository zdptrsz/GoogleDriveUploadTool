﻿Imports Google.Apis.Auth.OAuth2
Imports Google.Apis.Drive.v3
Imports Google.Apis.Services
Imports Google.Apis.Util.Store
Imports System.IO
Imports System.Threading
Imports Google.Apis.Upload
Imports Google.Apis.Download

Public Class Form1
    Private FileIdsListBox As New ListBox
    Private FileSizeListBox As New ListBox
    Public pageToken As String = ""
    ' If modifying these scopes, delete your previously saved credentials
    ' at ~/.credentials/drive-dotnet-quickstart.json
    Shared Scopes As String() = {DriveService.Scope.DriveFile, DriveService.Scope.Drive}
    Shared ApplicationName As String = "Google Drive Uploader Tool"
    Public service As DriveService
    Private ResumeUpload As Boolean = False
    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        If String.IsNullOrEmpty(My.Settings.Language) Then
            My.Settings.Language = "English"
            My.Settings.Save()
            RadioButton1.Checked = True
            EnglishLanguage()
        Else
            If My.Settings.Language = "English" Then
                EnglishLanguage()
                RadioButton1.Checked = True
            Else
                SpanishLanguage()
                RadioButton2.Checked = True
            End If
        End If
        Dim credential As UserCredential

        Using stream = New FileStream("client_secret.json", FileMode.Open, FileAccess.Read)
            Dim credPath As String = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal)
            credPath = Path.Combine(credPath, ".credentials/GoogleDriveUploaderTool.json")
            credential = GoogleWebAuthorizationBroker.AuthorizeAsync(GoogleClientSecrets.Load(stream).Secrets, Scopes, "user", CancellationToken.None, New FileDataStore(credPath, True)).Result
        End Using
        ' Create Drive API service.
        Dim Initializer As New BaseClientService.Initializer()
        Initializer.HttpClientInitializer = credential
        Initializer.ApplicationName = ApplicationName
        service = New DriveService(Initializer)
        service.HttpClient.Timeout = TimeSpan.FromSeconds(240)
        ' List files.
        ShowList()
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        ShowList()
    End Sub
    Private Sub ShowList()
        Dim listRequest As FilesResource.ListRequest = service.Files.List()
        listRequest.PageSize = 25
        listRequest.Fields = "nextPageToken, files(id, name, size)"
        listRequest.PageToken = pageToken
        ' List files.
        Try
            Dim files = listRequest.Execute()
            If files.Files IsNot Nothing AndAlso files.Files.Count > 0 Then
                For Each file In files.Files
                    ListBox1.Items.Add(file.Name)
                    FileIdsListBox.Items.Add(file.Id)
                    Try
                        FileSizeListBox.Items.Add(file.Size)
                    Catch
                        FileSizeListBox.Items.Add("0")
                    End Try
                Next
            End If
            pageToken = files.NextPageToken
        Catch ex As Exception
        End Try
    End Sub
    Private starttime As DateTime
    Private timespent As TimeSpan
    Private secondsremaining As Integer = 0
    Private GetFile As String = ""
    Private UploadFailed As Boolean = False
    Private Async Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        Dim NumberOfFilesToUpload As Integer = ListBox2.Items.Count
        For i As Integer = 0 To NumberOfFilesToUpload - 1
            UploadFailed = False
            GetFile = ListBox2.Items.Item(0)
            Label3.Text = String.Format("{0:F2} MB", My.Computer.FileSystem.GetFileInfo(GetFile).Length / 1024 / 1024)
            ProgressBar1.Maximum = My.Computer.FileSystem.GetFileInfo(GetFile).Length / 1024 / 1024
            Dim FileMetadata As New Data.File
            FileMetadata.Name = My.Computer.FileSystem.GetName(GetFile)
            Dim FileFolder As New List(Of String)
            If String.IsNullOrEmpty(TextBox2.Text) = False Then
                FileFolder.Add(TextBox2.Text)
            Else
                FileFolder.Add("root")
            End If
            FileMetadata.Parents = FileFolder
            Dim UploadStream As New FileStream(GetFile, System.IO.FileMode.Open, System.IO.FileAccess.Read)
            FileMetadata.ModifiedTime = IO.File.GetLastWriteTimeUtc(GetFile)
            Dim UploadFile As FilesResource.CreateMediaUpload = service.Files.Create(FileMetadata, UploadStream, "")
            UploadFile.ChunkSize = ResumableUpload.MinimumChunkSize * 4
            AddHandler UploadFile.ProgressChanged, New Action(Of IUploadProgress)(AddressOf Upload_ProgressChanged)
            AddHandler UploadFile.ResponseReceived, New Action(Of Data.File)(AddressOf Upload_ResponseReceived)
            AddHandler UploadFile.UploadSessionData, AddressOf Upload_UploadSessionData
            UploadCancellationToken = New CancellationToken
            Dim uploadUri As Uri = GetSessionRestartUri()
            starttime = DateTime.Now
            If uploadUri = Nothing Then
                Await UploadFile.UploadAsync(UploadCancellationToken)
            Else
                Await UploadFile.ResumeAsync(uploadUri, UploadCancellationToken)
            End If
            If UploadFailed = False Then
                ListBox2.Items.RemoveAt(0)
                RefreshFileList()
            End If
        Next
    End Sub
    Private ErrorMessage As String = ""
    Private UploadCancellationToken As System.Threading.CancellationToken
    Shared BytesSentText As Long
    Shared UploadStatusText As String
    Private Sub Upload_ProgressChanged(uploadStatusInfo As IUploadProgress)
        Select Case uploadStatusInfo.Status
            Case UploadStatus.Completed
                If RadioButton1.Checked = True Then UploadStatusText = "Completed!!" Else UploadStatusText = "¡Completado!"
                BytesSentText = My.Computer.FileSystem.GetFileInfo(GetFile).Length
                UpdateBytesSent()
            Case UploadStatus.Starting
                BytesSentText = "0"
                If RadioButton1.Checked = True Then UploadStatusText = "Starting..." Else UploadStatusText = "Comenzando..."
                UpdateBytesSent()
            Case UploadStatus.Uploading
                BytesSentText = uploadStatusInfo.BytesSent
                If RadioButton1.Checked = True Then UploadStatusText = "Uploading..." Else UploadStatusText = "Subiendo..."
                timespent = DateTime.Now - starttime
                Try
                    secondsremaining = (timespent.TotalSeconds / ProgressBar1.Value * (ProgressBar1.Maximum - ProgressBar1.Value))
                Catch
                    secondsremaining = 0
                End Try
                UpdateBytesSent()
            Case UploadStatus.Failed
                Dim APIException As Google.GoogleApiException = TryCast(uploadStatusInfo.Exception, Google.GoogleApiException)
                If (APIException Is Nothing) OrElse (APIException.Error Is Nothing) Then
                    MsgBox(uploadStatusInfo.Exception.Message)
                Else
                    MsgBox(APIException.Error.ToString())
                    ' Do not retry if the request is in error
                    Dim StatusCode As Int32 = CInt(APIException.HttpStatusCode)
                    ' See https://developers.google.com/youtube/v3/guides/using_resumable_upload_protocol
                    If ((StatusCode / 100) = 4 OrElse ((StatusCode / 100) = 5 AndAlso Not (StatusCode = 500 Or StatusCode = 502 Or StatusCode = 503 Or StatusCode = 504))) Then
                        If RadioButton1.Checked = True Then MsgBox("Cannot retry upload...") Else MsgBox("No se puede continuar subiendo este archivo desde el punto en que se interrumpió")
                    End If
                End If
                If RadioButton1.Checked = True Then UploadStatusText = "Failed..." Else UploadStatusText = "Error..."
                UploadFailed = True
                UpdateBytesSent()
        End Select
    End Sub
    Private Sub Upload_ResponseReceived(file As Data.File)
        If RadioButton1.Checked = True Then UploadStatusText = "Completed!!" Else UploadStatusText = "¡Completado!"
        BytesSentText = My.Computer.FileSystem.GetFileInfo(GetFile).Length
        UpdateBytesSent()

    End Sub
    Private Sub Upload_UploadSessionData(ByVal uploadSessionData As IUploadSessionData)
        ' Save UploadUri.AbsoluteUri and FullPath Filename values for use if program faults and we want to restart the program
        My.Settings.ResumeUri = uploadSessionData.UploadUri.AbsoluteUri
        My.Settings.ResumeFilename = GetFile
        ' Saved to a user.config file within a subdirectory of C:\Users\<yourusername>\AppData\Local
        My.Settings.Save()

    End Sub
    Private Function GetSessionRestartUri() As Uri
        If My.Settings.ResumeUri.Length > 0 AndAlso My.Settings.ResumeFilename = GetFile Then
            ' An UploadUri from a previous execution is present, ask if a resume should be attempted
            Dim ResumeText1 As String = ""
            Dim ResumeText2 As String = ""

            If RadioButton1.Checked = True Then
                ResumeText1 = "Resume previous upload?{0}{0}{1}"
                ResumeText2 = "Resume Upload"
            Else
                ResumeText1 = "¿Resumir carga anterior?{0}{0}{1}"
                ResumeText2 = "Resumir"
            End If

            If MsgBox(String.Format(ResumeText1, vbNewLine, GetFile), MsgBoxStyle.Question Or MsgBoxStyle.YesNo, ResumeText2) = MsgBoxResult.Yes Then
                Return New Uri(My.Settings.ResumeUri)
            Else
                Return Nothing
            End If
        Else
            Return Nothing
        End If
    End Function
    Private Sub UpdateBytesSent()
        If Label4.InvokeRequired Then
            Dim method As MethodInvoker = New MethodInvoker(AddressOf UpdateBytesSent)
            Invoke(method)
        End If
        If Label8.InvokeRequired Then
            Dim method As MethodInvoker = New MethodInvoker(AddressOf UpdateBytesSent)
            Invoke(method)
        End If
        If Label14.InvokeRequired Then
            Dim method As MethodInvoker = New MethodInvoker(AddressOf UpdateBytesSent)
            Invoke(method)
        End If
        If ProgressBar1.InvokeRequired Then
            Dim method As MethodInvoker = New MethodInvoker(AddressOf UpdateBytesSent)
            Invoke(method)
        End If
        Label4.Text = String.Format("{0:F2} MB", BytesSentText / 1024 / 1024)
        Label8.Text = UploadStatusText
        Try
            ProgressBar1.Value = BytesSentText / 1024 / 1024
        Catch

        End Try
        Label10.Text = String.Format("{0:F2}%", ((ProgressBar1.Value / ProgressBar1.Maximum) * 100))
        Dim timeFormatted As TimeSpan = TimeSpan.FromSeconds(secondsremaining)
        Label14.Text = String.Format("{0}:{1:mm}:{1:ss}", CInt(Math.Truncate(timeFormatted.TotalHours)), timeFormatted)
    End Sub
    Private Shared FileToSave As FileStream
    Private Shared MaxFileSize
    Private Sub Button5_Click(sender As Object, e As EventArgs) Handles Button5.Click
        FileIdsListBox.SelectedIndex = ListBox1.SelectedIndex
        FileSizeListBox.SelectedIndex = ListBox1.SelectedIndex
        If RadioButton1.Checked = True Then
            SaveFileDialog1.Title = "Browse for a location to save the file:"
        Else
            SaveFileDialog1.Title = "Busque un lugar para descargar el archivo:"
        End If
        SaveFileDialog1.FileName = ListBox1.SelectedItem
        Dim SFDResult As MsgBoxResult = SaveFileDialog1.ShowDialog()
        If SFDResult = MsgBoxResult.Ok Then
            starttime = DateTime.Now
            Label3.Text = String.Format("{0:F2} MB", FileSizeListBox.SelectedItem / 1024 / 1024)
            ProgressBar1.Maximum = FileSizeListBox.SelectedItem / 1024 / 1024
            MaxFileSize = FileSizeListBox.SelectedItem
            FileToSave = New FileStream(SaveFileDialog1.FileName, FileMode.Create, FileAccess.Write)
            Dim DownloadRequest As FilesResource.GetRequest = service.Files.Get(FileIdsListBox.SelectedItem.ToString)
            AddHandler DownloadRequest.MediaDownloader.ProgressChanged, New Action(Of IDownloadProgress)(AddressOf Download_ProgressChanged)
            DownloadRequest.DownloadAsync(FileToSave)
        End If
    End Sub
    Private Sub Download_ProgressChanged(progress As IDownloadProgress)
        Select Case progress.Status
            Case DownloadStatus.Completed
                If RadioButton1.Checked = True Then UploadStatusText = "Completed!!" Else UploadStatusText = "¡Completado!"
                FileToSave.Close()
                BytesSentText = MaxFileSize
                UpdateBytesSent()

            Case DownloadStatus.Downloading
                BytesSentText = progress.BytesDownloaded
                If RadioButton1.Checked = True Then UploadStatusText = "Downloading..." Else UploadStatusText = "Descargando..."
                UpdateBytesSent()
            Case UploadStatus.Failed
                If RadioButton1.Checked = True Then UploadStatusText = "Failed..." Else UploadStatusText = "Error..."
                UpdateBytesSent()
        End Select
    End Sub

    Private Sub Button4_Click(sender As Object, e As EventArgs) Handles Button4.Click
        RefreshFileList()
    End Sub

    Private Sub RefreshFileList()
        If ListBox1.InvokeRequired Then
            Dim method As MethodInvoker = New MethodInvoker(AddressOf RefreshFileList)
            Invoke(method)
        End If
        ListBox1.Items.Clear()
        FileIdsListBox.Items.Clear()
        FileSizeListBox.Items.Clear()
        Dim listRequest As FilesResource.ListRequest = service.Files.List()
        listRequest.PageSize = 25
        listRequest.Fields = "nextPageToken, files(id, name, size)"
        listRequest.PageToken = ""
        ' List files.
        Try
            Dim files = listRequest.Execute()
            If files.Files IsNot Nothing AndAlso files.Files.Count > 0 Then
                For Each file In files.Files
                    ListBox1.Items.Add(file.Name)
                    FileIdsListBox.Items.Add(file.Id)
                    Try
                        FileSizeListBox.Items.Add(file.Size)
                    Catch
                        FileSizeListBox.Items.Add("0")
                    End Try
                Next
            End If
        Catch ex As Exception
        End Try
    End Sub
    Private Sub Form1_DragDrop(sender As Object, e As DragEventArgs) Handles Me.DragDrop
        Dim filepath() As String = e.Data.GetData(DataFormats.FileDrop)
        For Each path In filepath
            ListBox2.Items.Add(path)
        Next
    End Sub
    Private Sub Form1_DragEnter(sender As System.Object, e As System.Windows.Forms.DragEventArgs) Handles Me.DragEnter
        If e.Data.GetDataPresent(DataFormats.FileDrop) Then
            e.Effect = DragDropEffects.Copy
        End If
    End Sub

    Private Sub RadioButton1_CheckedChanged(sender As Object, e As EventArgs) Handles RadioButton1.CheckedChanged
        EnglishLanguage()
        My.Settings.Language = "English"
        My.Settings.Save()
    End Sub

    Private Sub RadioButton2_CheckedChanged(sender As Object, e As EventArgs) Handles RadioButton2.CheckedChanged
        SpanishLanguage()
        My.Settings.Language = "Spanish"
        My.Settings.Save()
    End Sub
    Private Sub EnglishLanguage()
        Label1.Text = "Length:"
        Label2.Text = "Processed:"
        Label5.Text = "Drag and Drop Files to add them to the list"
        Label6.Text = "By Moises Cardona" & vbNewLine & "v1.4"
        Label7.Text = "Status:"
        Label9.Text = "Percent: "
        Label11.Text = "Uploads (By Date Modified):"
        Label12.Text = "Upload to this folder ID (""root"" to upload to root folder):"
        Label13.Text = "Time Left: "
        Button1.Text = "More Results"
        Button2.Text = "Upload"
        Button4.Text = "Refresh List"
        Button5.Text = "Download File"
        Button6.Text = "Remove selected file from list"
    End Sub
    Private Sub SpanishLanguage()
        Label1.Text = "Tamaño:"
        Label2.Text = "Procesado:"
        Label5.Text = "Arrastre archivos aquí para añadirlos a la lista"
        Label6.Text = "Por Moises Cardona" & vbNewLine & "v1.4"
        Label7.Text = "Estado:"
        Label9.Text = "Porcentaje: "
        Label11.Text = "Archivos subidos (Organizados por fecha de modificación):"
        Label12.Text = "Subir a este ID de directorio (""root"" para subir a la raíz):"
        Label13.Text = "Tiempo Est."
        Button1.Text = "Más Resultados"
        Button2.Text = "Subir"
        Button4.Text = "Refrescar Lista"
        Button5.Text = "Descargar Archivo"
        Button6.Text = "Remover archivo de la lista"
    End Sub

    Private Sub Button6_Click(sender As Object, e As EventArgs) Handles Button6.Click
        ListBox2.Items.RemoveAt(ListBox2.SelectedIndex)
    End Sub
End Class