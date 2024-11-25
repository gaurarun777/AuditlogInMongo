$destinationFolder = "C:\inetpub\wwwroot\sc104sc.dev.local\App_Data\logs\audit"
$custom_folder_name = "old"

$finalPath = $destinationFolder+'\'+$custom_folder_name

if (!(Test-Path -path $finalPath)) {New-Item $finalPath -Type Directory}

#Copy-Item -Path "C:\inetpub\wwwroot\sc104sc.dev.local\App_Data\logs\audit\*.txt" -Destination $destinationFolder
Copy-Item -Path $destinationFolder"\*.txt" -Destination $finalPath