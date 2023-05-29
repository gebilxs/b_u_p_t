using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using Unity.WebRTC;

public class RTCIceCandidateConverter : JsonConverter<RTCIceCandidate>
{
    public override RTCIceCandidate ReadJson(JsonReader reader, Type objectType, RTCIceCandidate existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);
        var candidateInfo = jObject.ToObject<RTCIceCandidateInit>(serializer);

        if (candidateInfo.sdpMLineIndex == null && candidateInfo.sdpMid == null)
        {
            candidateInfo.sdpMLineIndex = 0; // 或者提供一个合适的值
            candidateInfo.sdpMid = "0"; // 或者提供一个合适的值
        }

        return new RTCIceCandidate(candidateInfo);
    }

    public override void WriteJson(JsonWriter writer, RTCIceCandidate value, JsonSerializer serializer)
    {
        var candidateInfo = new RTCIceCandidateInit
        {
            candidate = value.Candidate,
            sdpMid = value.SdpMid,
            sdpMLineIndex = value.SdpMLineIndex,
        };

        var jObject = JObject.FromObject(candidateInfo, serializer);
        jObject.WriteTo(writer);
    }
}