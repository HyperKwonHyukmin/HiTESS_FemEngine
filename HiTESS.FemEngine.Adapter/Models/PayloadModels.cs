using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HiTESS.FemEngine.Adapter.Models
{
  public class InputPayload
  {
    [JsonPropertyName("metadata")] public MetadataPayload Metadata { get; set; }
    [JsonPropertyName("model")] public AnalysisPayload Model { get; set; }
  }

  public class MetadataPayload
  {
    [JsonPropertyName("module")] public string Module { get; set; }
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
  }

  public class AnalysisPayload
  {
    [JsonPropertyName("beamType")] public string BeamType { get; set; }
    [JsonPropertyName("dimensions")] public DimensionsPayload Dimensions { get; set; }
    [JsonPropertyName("boundaries")] public List<BoundaryPayload> Boundaries { get; set; }
    [JsonPropertyName("loads")] public List<LoadPayload> Loads { get; set; }
  }

  public class DimensionsPayload
  {
    [JsonPropertyName("length")] public double Length { get; set; }
    [JsonPropertyName("dim1")] public double Dim1 { get; set; }
    [JsonPropertyName("dim2")] public double Dim2 { get; set; }
    [JsonPropertyName("dim3")] public double Dim3 { get; set; }
    [JsonPropertyName("dim4")] public double Dim4 { get; set; }
  }

  public class BoundaryPayload
  {
    [JsonPropertyName("position")] public double Pos { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; }
  }

  public class LoadPayload
  {
    [JsonPropertyName("position")] public double Pos { get; set; }
    [JsonPropertyName("magnitude")] public double Magnitude { get; set; }
  }

  // ========================================================
  // ★ [추가] 차트 시각화를 위한 결과 배열 구조체
  // ========================================================
  public class NodeResultData
  {
    [JsonPropertyName("nodeId")] public int NodeId { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("dispZ")] public double DispZ { get; set; }
  }

  public class ElementResultData
  {
    [JsonPropertyName("elementId")] public int ElementId { get; set; }
    [JsonPropertyName("maxStress")] public double MaxStress { get; set; }
  }

  public class ResultPayload
  {
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("maxStress")] public double MaxStress { get; set; }
    [JsonPropertyName("maxDisp")] public double MaxDisp { get; set; }
    [JsonPropertyName("area")] public double Area { get; set; }
    [JsonPropertyName("inertia")] public double Inertia { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }

    [JsonPropertyName("nodeResults")] public List<NodeResultData> NodeResults { get; set; }
    [JsonPropertyName("elementResults")] public List<ElementResultData> ElementResults { get; set; }
  }
}