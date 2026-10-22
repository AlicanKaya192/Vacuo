using Vacuo.Core.Thermals;

namespace Vacuo.Windows.Tests;

public class GpuTemperatureReadingTests
{
    [Fact]
    public void ThisMachine_AnswersWithoutThrowing_AndOnlyPlausibleTemperaturesCount()
    {
        IReadOnlyList<AdapterReading> adapters = GpuTemperatureReader.Read();

        GpuTemperature? hottest = GpuTemperatureRule.Hottest(adapters);
        Assert.True(hottest is null or { Celsius: > 0 and < 150 });
    }

    // Compare with Task Manager or the vendor's tool (nvidia-smi --query-gpu=temperature.gpu --format=csv).
    [Fact(Explicit = true)]
    public void PrintThisMachine()
    {
        foreach (AdapterReading adapter in GpuTemperatureReader.Read())
        {
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"{adapter.Name}: {adapter.DeciCelsius} tenths, shown as {GpuTemperatureRule.FromDeciCelsius(adapter.DeciCelsius)?.ToString() ?? "not reported"}");
        }
    }
}
