using System;
using System.Text;
using System.IO;

using System.Collections.Generic;

using Popolo.ThermophysicalProperty;
using Popolo.HVAC.MultiplePackagedHeatPump;

namespace VRVSample
{
  internal class Program
  {
    //第1引数：室外機・室内機の構成ファイル、
    //第2引数：境界条件
    //第3引数：冷房の計算ならばCを入力、暖房ならばH
    static void Main(string[] args)
    {
      //debug
      //args = new string[] { "setting.csv", "boundary_H.csv", "h" };

      bool isCooling = (args[2] == "c" || args[2] == "C");
      NEDOTest(args[0], args[1], isCooling);
    }

    #region NEDO試験条件のテスト

    public static void NEDOTest(string setting, string boundary, bool isCooling)
    {

      //**モデル作成処理**************************************************************************************************************
      //冷媒物性計算インスタンス作成
      Refrigerant r410a = new Refrigerant(Refrigerant.Fluid.R410A);

      VRFSystem vrfSystem;
      VRFUnit[] iHexes;
      using (StreamReader sReader = new StreamReader(setting))
      {
        //室外機初期化用の室内機を読み込む
        sReader.ReadLine(); //ヘッダ
        string[] buff = sReader.ReadLine().Split(',');
        VRFUnit iHex = VRFSystem.MakeIndoorUnit(
          double.Parse(buff[0]) * 1.2 / 60d,
          double.Parse(buff[1]), -double.Parse(buff[2]), //冷房能力
          double.Parse(buff[3]), double.Parse(buff[4])); //暖房能力

        //室外機初期化
        sReader.ReadLine(); //ヘッダ
        buff = sReader.ReadLine().Split(',');
        vrfSystem = new VRFSystem(r410a,
          double.Parse(buff[0]) * 1.2 / 60d, double.Parse(buff[1]), //冷房風量、ファン消費電力
          -double.Parse(buff[2]), double.Parse(buff[3]), //冷房定格 
          -double.Parse(buff[4]), double.Parse(buff[5]), //冷房中間
          -double.Parse(buff[6]), double.Parse(buff[7]), //冷房中間中温
          double.Parse(buff[9]) * 1.2 / 60d, double.Parse(buff[10]), //暖房風量、ファン消費電力
          double.Parse(buff[11]), double.Parse(buff[12]), //暖房定格
          double.Parse(buff[13]), double.Parse(buff[14]), //暖房中間
          7.5, 100, double.Parse(buff[8]), 100, double.Parse(buff[15]), iHex);
        vrfSystem.MinimumPartialLoadRate = double.Parse(buff[16]);
        if (17 < buff.Length) vrfSystem.NumberOfOutdoorUnitDivisions = int.Parse(buff[17]);

        //室内機初期化
        sReader.ReadLine(); //ヘッダ
        string line;
        List<VRFUnit> ihs = new List<VRFUnit>();
        while ((line = sReader.ReadLine()) != null)
        {
          buff = line.Split(',');
          VRFUnit ih;
          if (5 < buff.Length && bool.Parse(buff[5]))
          {
            ih = new VRFUnit(
              double.Parse(buff[0]) * 1.2 / 60d,
              6.0, -double.Parse(buff[2]), 33, 0.0220, 99, double.Parse(buff[1]),
              46.0, double.Parse(buff[4]), 7, 0.0053, double.Parse(buff[3]));
          }
          else
          {
            ih = VRFSystem.MakeIndoorUnit(
              double.Parse(buff[0]) * 1.2 / 60d,
              double.Parse(buff[1]), -double.Parse(buff[2]),
              double.Parse(buff[3]), double.Parse(buff[4]));
          }
          ihs.Add(ih);
        }
        iHexes = ihs.ToArray();
      }

      vrfSystem.AddIndoorUnit(iHexes);
      vrfSystem.MinEvaporatingTeperature = 6;
      vrfSystem.MaxEvaporatingTeperature = 11;
      vrfSystem.MinCondensingTeperature = 41;
      vrfSystem.MaxCondensingTeperature = 46;
      vrfSystem.ControlThermoOffWithSensibleHeat = false; //全熱基準でサーモを制御（処理負荷を完全に合わせるため）

      //計算の実行
      if (isCooling) exeCooling(vrfSystem, iHexes, boundary);
      else exeHeating(vrfSystem, iHexes, boundary);
    }

    private static void exeCooling
      (VRFSystem vrfSystem, VRFUnit[] iHexes, string boundaryFile)
    {
      Console.WriteLine("Cooling mode test");
      vrfSystem.CurrentMode = VRFSystem.Mode.Cooling;
      for (int i = 0; i < iHexes.Length; i++)
        vrfSystem.SetIndoorUnitMode(i, VRFUnit.Mode.Cooling);
      using (StreamReader sReader = new StreamReader(boundaryFile))
      using (StreamWriter sWriter = new StreamWriter(boundaryFile.Remove(boundaryFile.Length - 4) + "_result.csv", false, Encoding.UTF8))
      {
        //最初の2行はヘッダ
        sReader.ReadLine();
        sReader.ReadLine();

        //書き出しファイルのヘッダ
        sWriter.Write("熱負荷[kW],処理負荷[kW],負荷率（実質）[-],低圧[MPa],高圧[MPa],圧縮機消費電力[kW],圧縮ヘッド[kW],室外機ファン消費電力[kW],室内機消費電力（合算）[kW],圧縮機COP[-],システムCOP[-]");
        for (int i = 0; i < iHexes.Length; i++) sWriter.Write(",室内機" + (i + 1) + "処理負荷[kW],室内機" + (i + 1) + "顕熱比[-]");
        sWriter.WriteLine();

        //CSVの最終行まで繰り返す
        string line;
        while ((line = sReader.ReadLine()) != null)
        {
          string[] buff = line.Split(',');

          //外気条件設定
          double oaDbt = double.Parse(buff[0]);
          vrfSystem.OutdoorAirDrybulbTemperature = oaDbt;
          vrfSystem.OutdoorAirHumidityRatio =
            MoistAir.GetHumidityRatioFromDryBulbTemperatureAndWetBulbTemperature(oaDbt, double.Parse(buff[1]), 101.325);

          //室内機条件の設定
          double loadSum = 0;
          for (int i = 0; i < iHexes.Length; i++)
          {
            //吸込空気状態
            double dbt_i = double.Parse(buff[3 * i + 3]);
            double hrt_i = MoistAir.GetHumidityRatioFromDryBulbTemperatureAndWetBulbTemperature(dbt_i, double.Parse(buff[3 * i + 4]), 101.325);
            vrfSystem.SetIndoorUnitInletAirState(i, dbt_i, hrt_i);
            //給気温度//処理負荷から逆算
            double cLoad = double.Parse(buff[3 * i + 2]);
            loadSum += cLoad;
            iHexes[i].SolveHeatLoad(-cLoad, iHexes[i].NominalAirFlowRate, dbt_i, hrt_i, false);
            vrfSystem.SetIndoorUnitSetpointTemperature(i, iHexes[i].OutletAirTemperature);
            vrfSystem.SetIndoorUnitSetpointHumidityRatio(i, iHexes[i].OutletAirHumidityRatio);
          }
          vrfSystem.UpdateState();

          //書き出し
          double ttlHeat = 0;
          //処理負荷を集計
          for (int i = 0; i < iHexes.Length; i++)
            ttlHeat -= iHexes[i].HeatTransfer;

          //系統全体
          sWriter.Write(
            loadSum.ToString("F2") + "," +
            ttlHeat.ToString("F2") + "," +
            vrfSystem.PartialLoadRate.ToString("F3") + "," +
            (0.001 * vrfSystem.CompressorInletPressure).ToString("F3") + "," +
            (0.001 * vrfSystem.CompressorOutletPressure).ToString("F3") + "," +
            vrfSystem.CompressorElectricity.ToString("F2") + "," +
            (vrfSystem.NominalHead_C * vrfSystem.PartialLoadRate).ToString("F2") + "," +
            vrfSystem.OutdoorUnitFanElectricity.ToString("F2") + "," +
            vrfSystem.IndoorUnitFanElectricity.ToString("F2") + "," +
            (loadSum == 0 ? 0 : (loadSum / vrfSystem.CompressorElectricity).ToString("F2")) + "," +
            (loadSum == 0 ? 0 : (loadSum / (vrfSystem.CompressorElectricity + vrfSystem.OutdoorUnitFanElectricity + vrfSystem.IndoorUnitFanElectricity)).ToString("F2"))
            );
          //室内機ごと
          for (int i = 0; i < iHexes.Length; i++)
          {
            double shf = iHexes[i].HeatTransfer == 0 ? 0 : (iHexes[i].SensibleHeatTransfer / iHexes[i].HeatTransfer);
            sWriter.Write(
              "," + (-iHexes[i].HeatTransfer).ToString("F2") +
              "," + shf.ToString("F3"));
          }
          sWriter.WriteLine();

        }
      }
    }

    private static void exeHeating
      (VRFSystem vrfSystem, VRFUnit[] iHexes, string boundaryFile)
    {
      Console.WriteLine("Heating mode test");
      vrfSystem.CurrentMode = VRFSystem.Mode.Heating;
      for (int i = 0; i < iHexes.Length; i++)
        vrfSystem.SetIndoorUnitMode(i, VRFUnit.Mode.Heating);
      using (StreamReader sReader = new StreamReader(boundaryFile))
      using (StreamWriter sWriter = new StreamWriter(boundaryFile.Remove(boundaryFile.Length - 4) + "_result.csv", false, Encoding.UTF8))
      {
        //最初の2行はヘッダ
        sReader.ReadLine();
        sReader.ReadLine();

        //書き出しファイルのヘッダ
        sWriter.Write("熱負荷[kW],処理負荷[kW],負荷率（実質）[-],低圧[MPa],高圧[MPa],圧縮機消費電力[kW],圧縮ヘッド[kW],室外機ファン消費電力[kW],室内機消費電力（合算）[kW],圧縮機COP[-],システムCOP[-]");
        for (int i = 0; i < iHexes.Length; i++) sWriter.Write(",室内機" + (i + 1) + "処理負荷[kW]");
        sWriter.WriteLine();

        //CSVの最終行まで繰り返す
        string line;
        while ((line = sReader.ReadLine()) != null)
        {
          string[] buff = line.Split(',');

          //外気条件設定
          double oaDbt = double.Parse(buff[0]);
          vrfSystem.OutdoorAirDrybulbTemperature = oaDbt;
          vrfSystem.OutdoorAirHumidityRatio =
            MoistAir.GetHumidityRatioFromDryBulbTemperatureAndWetBulbTemperature(oaDbt, double.Parse(buff[1]), 101.325);

          //室内機条件の設定
          double loadSum = 0;
          for (int i = 0; i < iHexes.Length; i++)
          {
            //吸込空気状態
            double dbt_i = double.Parse(buff[3 * i + 3]);
            double hrt_i = MoistAir.GetHumidityRatioFromDryBulbTemperatureAndWetBulbTemperature(dbt_i, double.Parse(buff[3 * i + 4]), 101.325);
            vrfSystem.SetIndoorUnitInletAirState(i, dbt_i, hrt_i);
            //給気温度//処理負荷から逆算
            double hLoad = double.Parse(buff[3 * i + 2]);
            loadSum += hLoad;
            iHexes[i].SolveHeatLoad(hLoad, iHexes[i].NominalAirFlowRate, dbt_i, hrt_i, false);
            vrfSystem.SetIndoorUnitSetpointTemperature(i, iHexes[i].OutletAirTemperature);
            vrfSystem.SetIndoorUnitSetpointHumidityRatio(i, iHexes[i].OutletAirHumidityRatio);
          }
          vrfSystem.UpdateState();

          //書き出し
          double ttlHeat = 0;
          //処理負荷を集計
          for (int i = 0; i < iHexes.Length; i++)
            ttlHeat += iHexes[i].HeatTransfer;

          //系統全体
          sWriter.Write(
            loadSum.ToString("F2") + "," +
            ttlHeat.ToString("F2") + "," +
            vrfSystem.PartialLoadRate.ToString("F3") + "," +
            (0.001 * vrfSystem.CompressorInletPressure).ToString("F3") + "," +
            (0.001 * vrfSystem.CompressorOutletPressure).ToString("F3") + "," +
            vrfSystem.CompressorElectricity.ToString("F2") + "," +
            (vrfSystem.NominalHead_H * vrfSystem.PartialLoadRate).ToString("F2") + "," +
            vrfSystem.OutdoorUnitFanElectricity.ToString("F2") + "," +
            vrfSystem.IndoorUnitFanElectricity.ToString("F2") + "," +
            (loadSum == 0 ? 0 : (loadSum / vrfSystem.CompressorElectricity).ToString("F2")) + "," +
            (loadSum == 0 ? 0 : (loadSum / (vrfSystem.CompressorElectricity + vrfSystem.OutdoorUnitFanElectricity + vrfSystem.IndoorUnitFanElectricity)).ToString("F2"))
            );
          //室内機ごと
          for (int i = 0; i < iHexes.Length; i++)
            sWriter.Write("," + (-iHexes[i].HeatTransfer).ToString("F2"));
          sWriter.WriteLine();

        }
      }
    }

    #endregion

  }
}
