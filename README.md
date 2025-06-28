# StorageDeviceMonitor
Attempt to disconnect storage devices using two separate whitelists

## Install / Usage
You need to compile the source code after downloading it first.
If Microsoft .NET Framework is installed, it is not necessary to install Visual Studio or other SDKs.
(This has been confirmed to work in a Windows 11 environment.)

1. Modify the paths for logs and whitelists in the downloaded source as needed.
2. Please search for "csc.exe" under "C:\Windows\Microsoft.NET".
```Shell
<path-to-csc>\csc.exe <download-cs-file.cs>
```
3. Store the generated .exe file in an appropriate directory where users in the Users group do not have write permissions.
4. Since "installutil.exe" is located in the same folder as "csc.exe", please check to confirm.
```Shell
<path-to-installutil>\installutil.exe <created-exe-file.exe>
```
5. From Windows Services, configure the following settings for the installed service:
   - Set the service logon account to Local System Account.
6. Start the service with no devices connected and output the recognized devices.
7. Dynamically update the whitelist file as needed.

## Uninstall
1. Please search for "installutil.exe" under "C:\Windows\Microsoft.NET".
```Shell
<path-to-installutil>\installutil.exe /uninstall <created-exe-file.exe>
```
2. After restarting the Windows system, delete the directory where the .exe file was stored.

## Acknowledgments
- [https://correct-log.com/bat_auto_admin/]
  in sample install batch
- [https://symfoware.blog.fc2.com/blog-entry-1132.html]
  service code sample
- 
