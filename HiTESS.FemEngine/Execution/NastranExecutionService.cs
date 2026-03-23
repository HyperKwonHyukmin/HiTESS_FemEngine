using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HiTESS.FemEngine.Core.Execution
{
  /// <summary>
  /// MSC/NX Nastran 솔버를 외부 프로세스로 실행하고, 
  /// 결과 파일(.f06)을 분석하여 해석 성공 여부 및 치명적 오류(FATAL)를 검출하는 서비스입니다.
  /// </summary>
  public static class NastranExecutionService
  {
    /// <summary>
    /// BDF 파일을 솔버로 해석하고 결과를 분석합니다.
    /// </summary>
    public static bool RunAndAnalyze(string bdfFilePath, Action<string> log)
    {
      if (!File.Exists(bdfFilePath))
      {
        log($"[실패] BDF 파일을 찾을 수 없습니다: {bdfFilePath}");
        return false;
      }

      string workDir = Path.GetDirectoryName(bdfFilePath)!;
      string fileName = Path.GetFileName(bdfFilePath);

      log($"\n[Nastran Run] 해석 솔버 구동을 시작합니다. (명령어: nastran {fileName})");

      try
      {
        var psi = new ProcessStartInfo
        {
          FileName = "cmd.exe",
          Arguments = $"/c nastran \"{fileName}\" bat=no",
          WorkingDirectory = workDir,
          UseShellExecute = false,
          CreateNoWindow = true
        };

        using (var process = Process.Start(psi))
        {
          process?.WaitForExit();
        }

        log($"[Nastran Run] 프로세스 종료됨. 결과 분석을 시작합니다...");

        string f06FilePath = Path.ChangeExtension(bdfFilePath, ".f06");
        return AnalyzeF06FileStream(f06FilePath, log);
      }
      catch (Exception ex)
      {
        log($"[실패] Nastran 프로세스 실행 중 예외 발생: {ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// 원형 버퍼(Circular Buffer)를 활용하여 메모리 낭비 없이 FATAL 에러 주변 컨텍스트를 추출합니다.
    /// </summary>
    private static bool AnalyzeF06FileStream(string f06FilePath, Action<string> log)
    {
      if (!File.Exists(f06FilePath))
      {
        log($"[실패] .f06 결과 파일이 생성되지 않았습니다.");
        return false;
      }

      int contextRange = 5;
      // 과거 로그 5줄을 보관하는 원형 큐
      var previousLines = new Queue<string>(contextRange);
      int fatalCount = 0;
      int linesToPrintAfterFatal = 0;
      int currentLineNumber = 0;

      foreach (var line in File.ReadLines(f06FilePath))
      {
        currentLineNumber++;

        // FATAL 발견
        if (line.Contains("FATAL MESSAGE") || line.Contains("*** FATAL"))
        {
          fatalCount++;

          log("\n------------------ [FATAL ERROR CONTEXT] ------------------");
          // 이전 5줄 출력
          int prevLineNum = currentLineNumber - previousLines.Count;
          foreach (var prevLine in previousLines)
          {
            log($"   Line {prevLineNum++:D5}: {prevLine}");
          }

          // FATAL 발생 라인 출력
          log($">> Line {currentLineNumber:D5}: {line}");

          // 앞으로 5줄 더 출력하도록 타이머 설정
          linesToPrintAfterFatal = contextRange;
        }
        // FATAL 이후 5줄 출력 중
        else if (linesToPrintAfterFatal > 0)
        {
          log($"   Line {currentLineNumber:D5}: {line}");
          linesToPrintAfterFatal--;
          if (linesToPrintAfterFatal == 0)
          {
            log("-----------------------------------------------------------\n");
          }
        }

        // 과거 컨텍스트 유지를 위해 원형 버퍼 관리
        if (previousLines.Count == contextRange)
        {
          previousLines.Dequeue();
        }
        previousLines.Enqueue(line);
      }

      if (fatalCount == 0)
      {
        log($"[통과] Nastran 해석 완료! .f06 파일 내 FATAL 오류가 없습니다.");
        return true;
      }

      log($"[실패] Nastran 해석 실패! .f06 파일에서 {fatalCount}개의 FATAL MESSAGE가 발견되었습니다.");
      return false;
    }
  }
}