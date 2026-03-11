param(
  [ValidateSet('win-x64','linux-x64')]
  [string]$Runtime = 'win-x64'
)

$project = "src/DnSpyMcpServer/DnSpyMcpServer.csproj"
$profile = if ($Runtime -eq 'win-x64') { 'win-x64' } else { 'linux-x64' }

dotnet publish $project -c Release -p:PublishProfile=$profile
