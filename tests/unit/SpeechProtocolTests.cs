using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Options;
using WePilot.Speech.Options;
using WePilot.Speech.Services;
using Xunit;
namespace WePilot.Tests;
public sealed class SpeechProtocolTests
{
    private readonly VolcengineAsrProtocol _protocol=new(Options.Create(new ArkSpeechOptions()));
    [Fact]
    public void FinalAudioPacketHasTerminalFlagAndCorrectSize()
    {
        var packet=_protocol.CreateAudioPacket(new byte[]{1,2,3,4},true);
        Assert.Equal(0x22,packet[1]);Assert.Equal(4,BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(4,4)));
        Assert.Equal(new byte[]{1,2,3,4},packet.Skip(8).ToArray());
    }
    [Fact]
    public void ServerFinalTranscriptKeepsTimeRange()
    {
        var json=Encoding.UTF8.GetBytes("{\"result\":{\"text\":\"测试\",\"utterances\":[{\"text\":\"测试\",\"definite\":true,\"start_time\":0,\"end_time\":500}]}}");
        var packet=new byte[8+json.Length];packet[0]=0x11;packet[1]=0x90;packet[2]=0x10;BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4,4),json.Length);json.CopyTo(packet,8);
        var result=Assert.Single(_protocol.ParseServerPacket(packet));Assert.Equal("final",result.Type);Assert.Equal("测试",result.Text);Assert.Equal(500,result.EndTime);
    }
    [Fact]
    public void ShortPacketIsReportedAsProtocolError()
    {
        Assert.Equal("error",Assert.Single(_protocol.ParseServerPacket(new byte[]{0x11})).Type);
    }
}
