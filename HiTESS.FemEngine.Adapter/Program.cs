using HiTESS.FemEngine.Adapter.Models;
using HiTESS.FemEngine.Core.Builders;
using HiTESS.FemEngine.Core.Entities;
using HiTESS.FemEngine.Core.Execution;
using HiTESS.FemEngine.Core.Exporter;
using HiTESS.FemEngine.Core.Modifiers;
using HiTESS.FemEngine.Core.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HiTESS.FemEngine.Adapter
{
  /// <summary>
  /// FEM Engine CLI 어댑터 진입점
  /// </summary>
  class Program
  {
    static int Main(string[] args)
    {
      if (args.Length < 2)
      {
        Console.WriteLine("Usage: FemEngine.exe <jsonPath> <workDir> [runNastran: true/false]");
        return 1;
      }

      string jsonPath = args[0];
      string workDir = args[1];
      bool runNastran = args.Length < 3 || (bool.TryParse(args[2], out bool parsed) && parsed);

      return ExecuteAnalysisPipeline(jsonPath, workDir, runNastran);
    }

    /// <summary>
    /// JSON 데이터를 기반으로 FE 모델을 생성하고, BDF 추출 및 Nastran 해석을 오케스트레이션합니다.
    /// </summary>
    private static int ExecuteAnalysisPipeline(string jsonPath, string workDir, bool runNastran)
    {
      string baseName = Path.GetFileNameWithoutExtension(jsonPath);
      string bdfPath = Path.Combine(workDir, $"{baseName}.bdf");
      string resultJsonPath = Path.Combine(workDir, $"{baseName}_Result.json");
      string dispCsvPath   = Path.Combine(workDir, $"{baseName}_disp.csv");
      string stressCsvPath = Path.Combine(workDir, $"{baseName}_stress.csv");
      string forceCsvPath  = Path.Combine(workDir, $"{baseName}_force.csv");

      var resultOut = new ResultPayload { Status = "Failed", Message = "Unknown Error" };

      try
      {
        // 1. 페이로드 로드 및 검증
        var payload = LoadAndValidatePayload(jsonPath);

        // 2. FE 모델 빌드
        var context = BuildFeModel(payload);

        // 3. 매핑 데이터 추출
        var spcMapped = MapBoundaries(payload, context);
        var forceMapped = MapLoads(payload, context);

        // 4. BDF Export
        BdfExporter.Export(context, workDir, $"{baseName}.bdf", spcMapped, forceMapped);

        // 5. 솔버 실행 및 결과 파싱
        if (runNastran)
        {
          Console.WriteLine("[Pipeline] Nastran 솔버 해석을 시작합니다...");
          if (!NastranExecutionService.RunAndAnalyze(bdfPath, Console.WriteLine))
          {
            resultOut.Message = "Nastran 해석 중 FATAL 에러 발생";
            WriteResult(resultJsonPath, resultOut);
            return 2; // 솔버 에러
          }

          ParseAndSaveResults(bdfPath, context, resultOut, dispCsvPath, stressCsvPath, forceCsvPath);
        }
        else
        {
          Console.WriteLine("[Pipeline] Nastran 솔버 실행 옵션이 꺼져있어 BDF 파일만 생성합니다.");
          resultOut.Status = "Skipped";
          resultOut.Message = "BDF Exported Successfully. Nastran execution was skipped.";
        }

        WriteResult(resultJsonPath, resultOut);
        return 0; // 성공
      }
      catch (Exception ex)
      {
        Console.WriteLine($"[Error] {ex.Message}");
        resultOut.Message = ex.Message;
        WriteResult(resultJsonPath, resultOut);
        return -1; // 시스템 예외
      }
    }

    private static AnalysisPayload LoadAndValidatePayload(string jsonPath)
    {
      string jsonString = File.ReadAllText(jsonPath);
      var input = JsonSerializer.Deserialize<InputPayload>(jsonString);
      var payload = input?.Model;
      ValidatePayload(payload);
      return payload;
    }

    private static FeModelContext BuildFeModel(AnalysisPayload payload)
    {
      var builder = new ComponentWizardBuilder();
      double[] dims = { payload.Dimensions.Dim1, payload.Dimensions.Dim2, payload.Dimensions.Dim3, payload.Dimensions.Dim4 };
      var boundaries = payload.Boundaries.Select(b => (b.Pos, b.Type)).ToList();
      var loads = payload.Loads.Select(l => (l.Pos, l.Magnitude)).ToList();

      var context = builder.BuildModel(payload.BeamType, payload.Dimensions.Length, dims, boundaries, loads);

      // 50mm 간격 자동 메싱
      ElementMeshingModifier.Run(context, 50.0, Console.WriteLine);

      return context;
    }

    private static List<(int NodeID, string Type)> MapBoundaries(AnalysisPayload payload, FeModelContext context)
    {
      return payload.Boundaries
          .Select(b => (NodeID: context.Nodes.FindClosestNodeID(b.Pos, 0, 0), Type: b.Type))
          .Where(x => x.NodeID > 0)
          .ToList();
    }

    private static List<(int NodeID, double Magnitude)> MapLoads(AnalysisPayload payload, FeModelContext context)
    {
      return payload.Loads
          .Select(l => (NodeID: context.Nodes.FindClosestNodeID(l.Pos, 0, 0), Magnitude: l.Magnitude))
          .Where(x => x.NodeID > 0)
          .ToList();
    }

    private static void ParseAndSaveResults(string bdfPath, FeModelContext context, ResultPayload resultOut, string dispCsvPath, string stressCsvPath, string forceCsvPath)
    {
      string f06Path = Path.ChangeExtension(bdfPath, ".f06");
      var (maxStress, maxDisp, nodeResults, stressResults, forceResults) = F06Parser.Parse(f06Path, context);

      var prop = context.Properties[context.Properties.Keys.First()];

      resultOut.Status         = "Success";
      resultOut.Message        = "Analysis Completed";
      resultOut.MaxStress      = maxStress;
      resultOut.MaxDisp        = maxDisp;
      resultOut.Area           = PropertyDimensionHelper.ComputeArea(prop);
      resultOut.Inertia        = PropertyDimensionHelper.ComputeMomentOfInertia(prop);
      resultOut.NodeResults    = nodeResults;
      resultOut.ElementResults = stressResults;
      resultOut.ForceResults   = forceResults;

      WriteCsv(dispCsvPath, stressCsvPath, forceCsvPath, nodeResults, stressResults, forceResults);
      Console.WriteLine($"[Pipeline] CSV 결과 저장 완료: {dispCsvPath}, {stressCsvPath}, {forceCsvPath}");
    }

    private static void WriteCsv(
      string dispCsvPath, string stressCsvPath, string forceCsvPath,
      List<NodeResultData> nodeResults,
      List<ElementResultData> stressResults,
      List<BeamForceData> forceResults)
    {
      var sb = new StringBuilder(1024 * 10);

      // 변위 CSV
      sb.AppendLine("NodeId,X[mm],DispZ[mm]");
      foreach (var n in nodeResults.OrderBy(n => n.X))
        sb.AppendLine($"{n.NodeId},{n.X:G6},{n.DispZ:G6}");
      File.WriteAllText(dispCsvPath, sb.ToString(), Encoding.UTF8);

      // 응력 CSV
      sb.Clear();
      sb.AppendLine("ElementId,Dist,SXC[MPa],SXD[MPa],SXE[MPa],SXF[MPa],S-MAX[MPa],S-MIN[MPa]");
      foreach (var e in stressResults.OrderBy(e => e.ElementId).ThenBy(e => e.Dist))
        sb.AppendLine($"{e.ElementId},{e.Dist:G6},{e.SXC:G6},{e.SXD:G6},{e.SXE:G6},{e.SXF:G6},{e.SMax:G6},{e.SMin:G6}");
      File.WriteAllText(stressCsvPath, sb.ToString(), Encoding.UTF8);

      // 내력 CSV
      sb.Clear();
      sb.AppendLine("ElementId,Dist,BendingMoment1,BendingMoment2,ShearForce1,ShearForce2,AxialForce,Torque,WarpingTorque");
      foreach (var f in forceResults.OrderBy(f => f.ElementId).ThenBy(f => f.Dist))
        sb.AppendLine($"{f.ElementId},{f.Dist:G6},{f.BendingMoment1:G6},{f.BendingMoment2:G6},{f.ShearForce1:G6},{f.ShearForce2:G6},{f.AxialForce:G6},{f.Torque:G6},{f.WarpingTorque:G6}");
      File.WriteAllText(forceCsvPath, sb.ToString(), Encoding.UTF8);
    }

    private static void ValidatePayload(AnalysisPayload payload)
    {
      if (payload == null) throw new ArgumentNullException(nameof(payload), "입력 JSON을 파싱할 수 없습니다.");
      if (string.IsNullOrWhiteSpace(payload.BeamType)) throw new ArgumentException("beam_type이 지정되지 않았습니다.");

      string[] validTypes = { "I", "H", "ROD", "TUBE", "L", "T", "CHAN", "BAR" };
      if (!validTypes.Contains(payload.BeamType.ToUpper()))
        throw new ArgumentException($"지원하지 않는 beam_type: '{payload.BeamType}'. 지원 타입: {string.Join(", ", validTypes)}");

      if (payload.Dimensions == null) throw new ArgumentException("dimensions가 지정되지 않았습니다.");
      if (payload.Dimensions.Length <= 0) throw new ArgumentException($"length는 0보다 커야 합니다. 입력값: {payload.Dimensions.Length}");

      var d = payload.Dimensions;
      switch (payload.BeamType.ToUpper())
      {
        case "I":
        case "H":
        case "L":
        case "T":
        case "CHAN":
          if (d.Dim1 <= 0 || d.Dim2 <= 0 || d.Dim3 <= 0 || d.Dim4 <= 0)
            throw new ArgumentException($"{payload.BeamType}형강은 모든 치수(dim1~dim4)가 0보다 커야 합니다.");
          break;
        case "ROD":
          if (d.Dim1 <= 0) throw new ArgumentException("ROD의 직경(dim1)은 0보다 커야 합니다.");
          break;
        case "TUBE":
          if (d.Dim1 <= 0 || d.Dim2 <= 0) throw new ArgumentException("TUBE의 외경(dim1)과 두께(dim2)는 0보다 커야 합니다.");
          if (d.Dim2 >= d.Dim1 / 2.0) throw new ArgumentException("TUBE의 두께(dim2)는 외경 반지름보다 작아야 합니다.");
          break;
        case "BAR":
          if (d.Dim1 <= 0 || d.Dim2 <= 0) throw new ArgumentException("BAR의 폭(dim1)과 높이(dim2)는 0보다 커야 합니다.");
          break;
      }

      if (payload.Boundaries == null) throw new ArgumentException("boundaries가 지정되지 않았습니다.");
      if (payload.Loads == null) throw new ArgumentException("loads가 지정되지 않았습니다.");

      foreach (var bc in payload.Boundaries)
      {
        if (bc.Pos < 0 || bc.Pos > payload.Dimensions.Length)
          throw new ArgumentException($"경계조건 위치 {bc.Pos}가 보 길이 범위 밖입니다.");
        if (string.IsNullOrWhiteSpace(bc.Type))
          throw new ArgumentException("경계조건 type이 지정되지 않았습니다.");
      }

      foreach (var load in payload.Loads)
      {
        if (load.Pos < 0 || load.Pos > payload.Dimensions.Length)
          throw new ArgumentException($"하중 위치 {load.Pos}가 보 길이 범위 밖입니다.");
      }
    }

    private static void WriteResult(string path, ResultPayload result)
    {
      string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json);
    }
  }
}