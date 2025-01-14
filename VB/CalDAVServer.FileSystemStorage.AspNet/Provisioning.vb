Imports System
Imports System.IO
Imports System.Web
Imports System.Configuration
Imports System.Security.Principal
Imports System.Security.AccessControl
Imports System.Threading.Tasks
Imports CalDAVServer.FileSystemStorage.AspNet.CalDav

''' <summary>
''' This class creates initial calendar(s) and address book(s) for user during first log-in.
''' </summary>
''' <remarks>
''' In case of windows authentication methods in this class are using impersonation. In 
''' case you run IIS Express and log-in as the user that is different from the one running 
''' IIS Express, the IIS Express must run with Administrative permissions.
''' </remarks>
Public Class Provisioning
    Implements IHttpModule

    ''' <summary>
    ''' Path to the folder which stores WebDAV files.
    ''' </summary>
    Private Shared ReadOnly repositoryPath As String = If(ConfigurationManager.AppSettings("RepositoryPath"), String.Empty)

    Public Sub Dispose() Implements IHttpModule.Dispose
    End Sub

    Public Sub Init(application As HttpApplication) Implements IHttpModule.Init
        Dim postAuthAsyncHelper As EventHandlerTaskAsyncHelper = New EventHandlerTaskAsyncHelper(AddressOf App_OnPostAuthenticateRequestAsync)
        application.AddOnPostAuthenticateRequestAsync(postAuthAsyncHelper.BeginEventHandler, postAuthAsyncHelper.EndEventHandler)
    End Sub

    Private Async Function App_OnPostAuthenticateRequestAsync(source As Object, eventArgs As EventArgs) As Task
        Dim httpContext As HttpContext = HttpContext.Current
        If(httpContext.User Is Nothing) OrElse Not httpContext.User.Identity.IsAuthenticated Then Return
        Dim context As DavContext = New DavContext(httpContext)
        ' Create calendars for the user during first log-in.
        Await CreateCalendarFoldersAsync(context)
    End Function

    ''' <summary>
    ''' Creates initial calendars for user as well as inbox and outbox folders.
    ''' </summary>
    Friend Shared Async Function CreateCalendarFoldersAsync(context As DavContext) As Task
        Dim physicalRepositoryPath As String = If(repositoryPath.StartsWith("~"), HttpContext.Current.Server.MapPath(repositoryPath), repositoryPath)
        ' Get path to user folder /calendars/[user_name]/ and check if it exists.
        Dim calendarsUserFolder As String = String.Format("{0}{1}", CalendarsRootFolder.CalendarsRootFolderPath.Replace("/"c, Path.DirectorySeparatorChar), context.UserName)
        Dim pathCalendarsUserFolder As String = Path.Combine(physicalRepositoryPath, calendarsUserFolder.TrimStart(Path.DirectorySeparatorChar))
        If Not Directory.Exists(pathCalendarsUserFolder) Then
            Directory.CreateDirectory(pathCalendarsUserFolder)
            ' Grant full control to loged-in user.
            GrantFullControl(pathCalendarsUserFolder, context)
            ' Create all subfolders under the logged-in user account
            ' so all folders has logged-in user as the owner.
            context.FileOperation(Sub()
                ' Make the loged-in user the owner of the new folder.
                MakeOwner(pathCalendarsUserFolder, context)
                ' Create user calendars, such as /calendars/[user_name]/Calendar/.
                Dim pathCalendar As String = Path.Combine(pathCalendarsUserFolder, "Calendar1")
                Directory.CreateDirectory(pathCalendar)
                pathCalendar = Path.Combine(pathCalendarsUserFolder, "Home1")
                Directory.CreateDirectory(pathCalendar)
                pathCalendar = Path.Combine(pathCalendarsUserFolder, "Work1")
                Directory.CreateDirectory(pathCalendar)
            End Sub)
        End If
    End Function

    ''' <summary>
    ''' Makes the loged-in user the owner of the folder.
    ''' </summary>
    ''' <param name="folderPath">folder path in file system</param>
    Private Shared Sub MakeOwner(folderPath As String, context As DavContext)
        Dim securityOwner As DirectorySecurity = Directory.GetAccessControl(folderPath, AccessControlSections.Owner)
        securityOwner.SetOwner(context.WindowsIdentity.User)
        Directory.SetAccessControl(folderPath, securityOwner)
    End Sub

    ''' <summary>
    ''' Grants full controll to currently loged-in user.
    ''' </summary>
    ''' <param name="folderPath">folder path in file system</param>
    Private Shared Sub GrantFullControl(folderPath As String, context As DavContext)
        Dim folder As DirectoryInfo = New DirectoryInfo(folderPath)
        Dim security As DirectorySecurity = folder.GetAccessControl()
        security.AddAccessRule(New FileSystemAccessRule(context.WindowsIdentity.User, FileSystemRights.FullControl, InheritanceFlags.ObjectInherit Or InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow))
        folder.SetAccessControl(security)
    End Sub
End Class
