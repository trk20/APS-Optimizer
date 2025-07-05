# Build script for regular publish (alternative to ClickOnce)
MSBuild.exe /m /r /target:Publish /p:Configuration=Release /p:PublishProfile="APS_Optimizer_V3\Properties\PublishProfiles\RegularPublish.pubxml" /p:TargetFramework=net8.0-desktop
