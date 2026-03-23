using HiTESS.FemEngine.Adapter.Models;
using HiTESS.FemEngine.Core.Builders;
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
  class Program
  {
    /// <summary>
    /// 프로그램 진입점입니다.
    /// 사용법: program.exe [jsonPath] [workDir] [runNastran(true/false, 생략시 true)]
    /// </summary>
    static int Main(string[] args)
    {
      if (args.Length < 2)
      {
        Console.WriteLine("Usage: FemEngine.exe <jsonPath> <workDir> [runNastran: true/false]");
        return 1;
      }

      string jsonPath = args[0];
      string workDir = args[1];

      // 세 번째 인자로 Nastran 실행 여부를 bool 옵션으로 받도록 처리 (기본값 true)
      bool runNastran = true;
      if (args.Length >= 3 && bool.TryParse(args[2], out bool parsedRunNastran))
      {
        runNastran = parsedRunNastran;
      }

      return ExecuteAnalysisPipeline(jsonPath, workDir, runNastran);
    }

    /// <summary>
    /// JSON 데이터를 기반으로 FE 모델을 생성하고, BDF 추출 및 Nastran 해석(선택적)을 수행합니다.
    /// </summary>
    /// <param name="jsonPath">입력 JSON 파일 경로</param>
    /// <param name="workDir">작업 디렉토리 경로</param>
    /// <param name="runNastran">Nastran 솔버 실행 여부 플래그</param>
    /// <returns>성공 시 0, 솔버 에러 시 2, 기타 예외 시 -1</returns>
    private static int ExecuteAnalysisPipeline(string jsonPath, string workDir, bool runNastran)
    {
      string baseName = Path.GetFileNameWithoutExtension(jsonPath);
      string bdfPath = Path.Combine(workDir, $"{baseName}.bdf");
      string resultJsonPath = Path.Combine(workDir, $"{baseName}_Result.json");
      string dispCsvPath = Path.Combine(workDir, $"{baseName}_disp.csv");
      string stressCsvPath = Path.Combine(workDir, $"{baseName}_stress.csv");

      var resultOut = new ResultPayload { Status = "Failed", Message = "Unknown Error" };

      try
      {
        string jsonString = File.ReadAllText(jsonPath);
        var input = JsonSerializer.Deserialize<InputPayload>(jsonString);
        var payload = input?.Model;
        ValidatePayload(payload);

        var builder = new ComponentWizardBuilder();
        double[] dims = { payload.Dimensions.Dim1, payload.Dimensions.Dim2, payload.Dimensions.Dim3, payload.Dimensions.Dim4 };

        var boundaries = payload.Boundaries.Select(b => (b.Pos, b.Type)).ToList();
        var loads = payload.Loads.Select(l => (l.Pos, l.Magnitude)).ToList();

        var context = builder.BuildModel(payload.BeamType, payload.Dimensions.Length, dims, boundaries, loads);

        // 50mm 간격 자동 메싱
        ElementMeshingModifier.Run(context, 50.0, Console.WriteLine);

        var spcMapped = payload.Boundaries.Select(b => (
            NodeID: context.Nodes.FindClosestNodeID(b.Pos, 0, 0), Type: b.Type
        )).Where(x => x.NodeID > 0).ToList();

        var forceMapped = payload.Loads.Select(l => (
            NodeID: context.Nodes.FindClosestNodeID(l.Pos, 0, 0), Magnitude: l.Magnitude
        )).Where(x => x.NodeID > 0).ToList();

        // 1. BDF 추출
        BdfExporter.Export(context, workDir, $"{baseName}.bdf", spcMapped, forceMapped);

        // 2. Nastran 해석 실행 여부 분기
        if (runNastran)
        {
          Console.WriteLine("[Pipeline] Nastran 솔버 해석을 시작합니다...");
          bool isSuccess = NastranExecutionService.RunAndAnalyze(bdfPath, Console.WriteLine);

          if (!isSuccess)
          {
            resultOut.Message = "Nastran 해석 중 FATAL 에러 발생";
            WriteResult(resultJsonPath, resultOut);
            return 2;
          }

          // F06 파싱 (배열 추출)
          string f06Path = Path.ChangeExtension(bdfPath, ".f06");
          var (maxStress, maxDisp, nodeResults, elemResults) = F06Parser.Parse(f06Path, context);

          var prop = context.Properties[context.Properties.Keys.First()];
          resultOut.Status = "Success";
          resultOut.Message = "Analysis Completed";
          resultOut.MaxStress = maxStress;
          resultOut.MaxDisp = maxDisp;
          resultOut.Area = PropertyDimensionHelper.ComputeArea(prop);
          resultOut.Inertia = PropertyDimensionHelper.ComputeMomentOfInertia(prop);
          resultOut.NodeResults = nodeResults;
          resultOut.ElementResults = elemResults;

          WriteCsv(dispCsvPath, stressCsvPath, nodeResults, elemResults);
          Console.WriteLine($"[Pipeline] CSV 결과 저장 완료: {dispCsvPath}, {stressCsvPath}");
        }
        else
        {
          // Nastran 실행 옵션이 꺼져있을 경우 BDF 추출까지만 수행하고 성공 처리
          Console.WriteLine("[Pipeline] Nastran 솔버 실행 옵션이 꺼져있어 BDF 파일만 생성합니다.");
          resultOut.Status = "Skipped";
          resultOut.Message = "BDF Exported Successfully. Nastran execution was skipped.";
        }

        WriteResult(resultJsonPath, resultOut);
        return 0;
      }
      catch (Exception ex)
      {
        resultOut.Message = ex.Message;
        WriteResult(resultJsonPath, resultOut);
        return -1;
      }
    }

    private static void WriteCsv(
      string dispCsvPath,
      string stressCsvPath,
      List<NodeResultData> nodeResults,
      List<ElementResultData> elemResults)
    {
      var sb = new StringBuilder();

      sb.AppendLine("NodeId,X[mm],DispZ[mm]");
      foreach (var n in nodeResults.OrderBy(n => n.X))
        sb.AppendLine($"{n.NodeId},{n.X:G6},{n.DispZ:G6}");
      File.WriteAllText(dispCsvPath, sb.ToString(), Encoding.UTF8);

      sb.Clear();
      sb.AppendLine("ElementId,MaxStress[MPa]");
      foreach (var e in elemResults.OrderBy(e => e.ElementId))
        sb.AppendLine($"{e.ElementId},{e.MaxStress:G6}");
      File.WriteAllText(stressCsvPath, sb.ToString(), Encoding.UTF8);
    }

    private static void ValidatePayload(AnalysisPayload payload)
    {
      if (payload == null)
        throw new ArgumentNullException(nameof(payload), "입력 JSON을 파싱할 수 없습니다.");

      if (string.IsNullOrWhiteSpace(payload.BeamType))
        throw new ArgumentException("beam_type이 지정되지 않았습니다.");

      string[] validTypes = { "I", "H", "ROD", "TUBE", "L", "T", "CHAN", "BAR" };
      if (!validTypes.Contains(payload.BeamType.ToUpper()))
        throw new ArgumentException($"지원하지 않는 beam_type: '{payload.BeamType}'. 지원 타입: {string.Join(", ", validTypes)}");

      if (payload.Dimensions == null)
        throw new ArgumentException("dimensions가 지정되지 않았습니다.");

      if (payload.Dimensions.Length <= 0)
        throw new ArgumentException($"length는 0보다 커야 합니다. 입력값: {payload.Dimensions.Length}");

      var d = payload.Dimensions;
      switch (payload.BeamType.ToUpper())
      {
        case "I":
        case "H":
          if (d.Dim1 <= 0 || d.Dim2 <= 0 || d.Dim3 <= 0 || d.Dim4 <= 0)
            throw new ArgumentException($"{payload.BeamType}형강은 모든 치수(dim1~dim4)가 0보다 커야 합니다.");
          break;
        case "ROD":
          if (d.Dim1 <= 0)
            throw new ArgumentException("ROD의 직경(dim1)은 0보다 커야 합니다.");
          break;
        case "TUBE":
          if (d.Dim1 <= 0 || d.Dim2 <= 0)
            throw new ArgumentException("TUBE의 외경(dim1)과 두께(dim2)는 0보다 커야 합니다.");
          if (d.Dim2 >= d.Dim1 / 2.0)
            throw new ArgumentException("TUBE의 두께(dim2)는 외경 반지름보다 작아야 합니다.");
          break;
        case "BAR":
          if (d.Dim1 <= 0 || d.Dim2 <= 0)
            throw new ArgumentException("BAR의 폭(dim1)과 높이(dim2)는 0보다 커야 합니다.");
          break;
        case "L":
        case "T":
        case "CHAN":
          if (d.Dim1 <= 0 || d.Dim2 <= 0 || d.Dim3 <= 0 || d.Dim4 <= 0)
            throw new ArgumentException($"{payload.BeamType}형강은 모든 치수(dim1~dim4)가 0보다 커야 합니다.");
          break;
      }

      if (payload.Boundaries == null)
        throw new ArgumentException("boundaries가 지정되지 않았습니다.");

      if (payload.Loads == null)
        throw new ArgumentException("loads가 지정되지 않았습니다.");

      foreach (var bc in payload.Boundaries)
      {
        if (bc.Pos < 0 || bc.Pos > payload.Dimensions.Length)
          throw new ArgumentException($"경계조건 위치 {bc.Pos}가 보 길이 범위(0~{payload.Dimensions.Length}) 밖입니다.");
        if (string.IsNullOrWhiteSpace(bc.Type))
          throw new ArgumentException("경계조건 type이 지정되지 않았습니다.");
      }

      foreach (var load in payload.Loads)
      {
        if (load.Pos < 0 || load.Pos > payload.Dimensions.Length)
          throw new ArgumentException($"하중 위치 {load.Pos}가 보 길이 범위(0~{payload.Dimensions.Length}) 밖입니다.");
      }
    }

    private static void WriteResult(string path, ResultPayload result)
    {
      string json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
      File.WriteAllText(path, json);
    }
  }
}