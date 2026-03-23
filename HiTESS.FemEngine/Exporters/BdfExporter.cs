using HiTESS.FemEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.IO;

namespace HiTESS.FemEngine.Core.Exporter
{
  public static class BdfExporter
  {
    public static void Export(
        FeModelContext context,
        string csvFolderPath,
        string outputFileName,
        List<(int NodeID, string Type)> spcList,
        List<(int NodeID, double Magnitude)> forceList)
    {
      var bdfBuilder = new BdfBuilder(101, context, spcList, forceList);
      bdfBuilder.Run();

      string bdfPath = Path.Combine(csvFolderPath, outputFileName);
      File.WriteAllLines(bdfPath, bdfBuilder.BdfLines);

      Console.WriteLine($"[Export] BDF 추출 완료: {outputFileName}");
    }
  }
}