using System.Text.Json;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;
public class DatasetSelectionTests
{
    [Fact] public void DatasetSelectionIsWhitelistedAndKeepsSmallFixtureIdentity()
    {
        var type=typeof(NativeDemoGateway);
        var resolve=type.GetMethod("ResolveDataset");
        Assert.NotNull(resolve);
        using var document=JsonDocument.Parse("{\"active\":\"large85\"}");
        Assert.Equal("large85",resolve!.Invoke(null,new object?[]{null,document.RootElement}));
        Assert.Equal("small13",resolve.Invoke(null,new object?[]{"small13",document.RootElement}));
        var invalid=Assert.Throws<System.Reflection.TargetInvocationException>(()=>resolve.Invoke(null,new object?[]{"../large85",document.RootElement}));
        Assert.IsType<InvalidOperationException>(invalid.InnerException);
        Assert.Equal("small13",type.GetProperty("DatasetId")?.GetValue(null));
        Assert.Equal("SIM-261019-13C",NativeDemoWorkspace.Version);
        Assert.Equal(12,NativeDemoWorkspace.Ids.Length);
    }
    [Fact] public void RuntimeIdentityMustMatchBeforeAnyRequestIsQueued()
    {
        var verify=typeof(NativeDemoGateway).GetMethod("VerifyRuntimeIdentity");
        Assert.NotNull(verify);
        Assert.Single(verify!.GetParameters());
        var directory=Path.Combine(Path.GetTempPath(),"aps-dataset-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var missing=Assert.Throws<System.Reflection.TargetInvocationException>(()=>verify.Invoke(null,new object?[]{directory}));
            Assert.IsType<InvalidOperationException>(missing.InnerException);
            var config=NativeDemoGateway.Config;
            var identity=new Dictionary<string,string?>{{"datasetId","small13"},{"demandVersion",config.GetProperty("demandVersion").GetString()},{"fixtureId",config.GetProperty("fixtureId").GetString()},{"inputFingerprint",config.GetProperty("inputFingerprint").GetString()}};
            var path=Path.Combine(directory,"identity.json");File.WriteAllText(path,JsonSerializer.Serialize(identity));
            verify.Invoke(null,new object?[]{directory});
            identity["datasetId"]="large85";File.WriteAllText(path,JsonSerializer.Serialize(identity));
            var wrong=Assert.Throws<System.Reflection.TargetInvocationException>(()=>verify.Invoke(null,new object?[]{directory}));
            Assert.IsType<InvalidOperationException>(wrong.InnerException);
            Assert.False(Directory.Exists(Path.Combine(directory,"requests")));
        }
        finally { Directory.Delete(directory,true); }
    }
}
