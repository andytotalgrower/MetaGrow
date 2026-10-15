using ApiModels.MetaGrow;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

public sealed partial class SoilReportCalculator
{
    /// <summary>Legacy LabSoil.CalculateInterimValues, run once per sample with every derived value recorded.</summary>
    private static Dictionary<string, double?> Derive(SoilReportLabSample sample, List<SoilCalculationStep> trace)
    {
        var values = new Dictionary<string, double?>(sample.Values, StringComparer.OrdinalIgnoreCase);
        double V(string key) => Value(values, key);
        void Set(string key, double value, string rule, string explanation, params (string Key, double Value)[] inputs)
        {
            Trace(trace, sample.LabSoilId, rule, key, explanation, value, inputs);
            values[key] = value;
        }
        foreach (var (element, factor) in new[] { ("Aluminium", 89.933333), ("Sodium", 229.9), ("Magnesium", 121.525),
                     ("Potassium", 390.98), ("NitricPotassium", 390.98), ("Calcium", 200.4), ("Iron", 279.25),
                     ("Manganese", 274.7), ("Copper", 317.75), ("Zinc", 326.95) })
        {
            var ppm = V(element + "Ppm"); var meq = V(element + "Meq");
            if (meq <= 0 && ppm > 0) Set(element + "Meq", ppm / factor, "ppm-to-meq", "Legacy elemental conversion factor.", ("ppm", ppm), ("factor", factor));
            else if (ppm <= 0 && meq > 0) Set(element + "Ppm", meq * factor, "meq-to-ppm", "Legacy elemental conversion factor.", ("meq", meq), ("factor", factor));
        }
        if (V("SodiumMeq") <= 0) Set("SodiumMeq", .02, "sodium-floor", "Legacy assumption when reported sodium is zero or missing.", ("reported", V("SodiumMeq")));
        var bases = new[] { "Potassium", "Magnesium", "Calcium", "Sodium", "Aluminium", "Zinc", "Copper", "Manganese", "Iron" };
        var baseSum = bases.Sum(element => V(element + "Meq"));
        Set("BaseSum", baseSum, "base-sum", "Sum major and trace cation equivalents.", bases.Select(element => (element + "Meq", V(element + "Meq"))).ToArray());
        Set("CecMeq", baseSum, "calculated-cec", "Legacy calculations replace the laboratory CEC with base sum.", ("labCEC", V("CecMeq")), ("baseSum", baseSum));
        var calculatedWater = V("PhCaCl2") > 0 ? V("PhCaCl2") + .8 : V("PhH2o");
        var calculatedCalcium = V("PhCaCl2") > 0 ? V("PhCaCl2") : V("PhH2o") - .8;
        var b64 = -.2379 * V("PhCaCl2") + 1.4792;
        var b66 = b64 > 0 ? ((1 / (1 - b64)) - 1) * baseSum : 0;
        Set("b66", b66, "exchange-acidity", "Legacy acidity estimate uses reported CaCl2 pH.", ("pHCaCl2", V("PhCaCl2")), ("b64", b64), ("baseSum", baseSum));
        var hydrogen = Math.Max(0, b66 - V("AluminiumMeq"));
        if (calculatedCalcium >= 6.2 || calculatedWater >= 7) hydrogen = 0;
        Set("ExchangeHPlus", hydrogen, "hydrogen-adjustment", "Subtract aluminium and apply the legacy pH cutoff.",
            ("b66", b66), ("aluminium", V("AluminiumMeq")), ("calculatedPHCaCl2", calculatedCalcium), ("calculatedPHWater", calculatedWater));
        var tec = baseSum + hydrogen;
        if (tec <= 0) throw new InvalidOperationException($"Sample {sample.LabSoilId}: cation data cannot produce a positive TEC.");
        Set("Tec", tec, "tec", "Base sum plus exchange hydrogen.", ("baseSum", baseSum), ("hydrogen", hydrogen));
        Set("ExchangeHPlusPc", hydrogen / tec * 100, "exchange-percent", "Hydrogen as a percentage of TEC.", ("hydrogen", hydrogen), ("TEC", tec));
        foreach (var element in new[] { "Sodium", "Potassium", "Calcium", "Magnesium", "Aluminium" })
            Set("Exchange" + element + "Pc", V(element + "Meq") / tec * 100, "exchange-percent", "Elemental equivalents as a percentage of TEC.", ("meq", V(element + "Meq")), ("TEC", tec));
        Set("TraceCationsPc", new[] { "Zinc", "Copper", "Manganese", "Iron" }.Sum(element => V(element + "Meq")) / tec * 100,
            "trace-cations", "Trace cations as a percentage of TEC.", ("TEC", tec));
        // Missing denominators must be visible to QA; do not silently emit Infinity or fabricate a ratio.
        foreach (var (key, numerator, denominator) in new[] { ("CalciumMagnesiumRatio", "ExchangeCalciumPc", "ExchangeMagnesiumPc"),
                     ("PotassiumMagnesiumRatio", "ExchangePotassiumPc", "ExchangeMagnesiumPc"),
                     ("CalciumMagnesiumPotassiumRatio", "CalciumMagnesiumRatio", "ExchangePotassiumPc") })
        {
            if (V(denominator) == 0)
            {
                values[key] = null;
                Trace(trace, sample.LabSoilId, "undefined-ratio", key, "A zero denominator leaves this ratio unavailable.", null, ("denominator", 0));
            }
            else Set(key, V(numerator) / V(denominator), "cation-ratio", "Legacy cation ratio.", ("numerator", V(numerator)), ("denominator", V(denominator)));
        }
        Set("OrganicCarbon", V("OrganicCarbonPc") > 0 ? V("OrganicCarbonPc") : V("OrganicMatterPc") / 1.72,
            "organic-carbon", "Use reported organic carbon, otherwise organic matter divided by 1.72.", ("reportedCarbon", V("OrganicCarbonPc")), ("organicMatter", V("OrganicMatterPc")));
        return values;
    }

    private static void AdjustBalances(List<TargetResult> targets, SoilReportTemplate template, Dictionary<string, double?> values,
        string crop, int sampleId, List<SoilCalculationStep> trace)
    {
        TargetResult? Get(string key) => targets.LastOrDefault(target => target.Rule.ObjectName == key);
        var mg = Get("ExchangeMagnesiumPc"); var ca = Get("ExchangeCalciumPc"); var na = Get("ExchangeSodiumPc");
        var sulphur = Get("SulphurPpm"); var ph = Get("PhH2o");
        void Change(TargetResult target, double amount, string rule, string explanation, int? productId = null, string? product = null)
        {
            Trace(trace, sampleId, rule, target.Rule.ObjectName, explanation, amount, ("before", target.Balance));
            target.Balance = amount;
            if (productId.HasValue) { target.ProductId = productId.Value; target.Product = product!; }
        }
        void Oxide(TargetResult target, string rule) => Change(target, target.Balance * .1 / .55, rule,
            "Replace dolomite (10% Mg) with magnesium oxide (55% Mg).", 1009, "Magnesium Oxide");
        if (crop.Contains("banana", StringComparison.OrdinalIgnoreCase))
        {
            if (mg is not null && ca is not null && mg.Balance > 0 && ca.Actual > ca.Target) Oxide(mg, "banana-calcium-high");
            if (ca?.ProductId == 5) Change(ca, ca.Balance + 500, "banana-lime", "Legacy banana rule adds 500 kg/ha lime.");
        }
        void OffsetDolomite(TargetResult? magnesium, TargetResult? calcium, bool requireLime)
        {
            if (magnesium?.ProductId != 6 || calcium is null || requireLime && calcium.ProductId != 5 || magnesium.Balance <= 0 || calcium.Balance <= 0) return;
            Change(calcium, Math.Max(0, calcium.Balance * .38 - magnesium.Balance * .2) / 38 * 100,
                "dolomite-calcium-offset", "Subtract calcium supplied by dolomite (20%) from lime (38%).");
        }
        OffsetDolomite(mg, ca, false);
        OffsetDolomite(Get("MagnesiumPpm"), Get("CalciumPpm"), true);
        if (ph is not null && ca is not null && na is not null && ph.Actual > 7 && na.Actual > 6)
        {
            var gypsum = na.Actual / 100 * 2 * Value(values, "Tec") * 200 * 2 * 6.45;
            Change(ca, gypsum, "sodium-gypsum", "High pH and sodium: replace calcium product with gypsum using the legacy sodium/TEC formula.", 9, "Gypsum");
            if (sulphur is not null) Change(sulphur, Math.Max(0, sulphur.Balance - gypsum * .125), "gypsum-sulphur-offset", "Subtract sulphur supplied by gypsum (12.5%).");
        }
        if (ph?.Actual >= 7.6 && mg?.ProductId == 6) Oxide(mg, "high-ph-magnesium");
        if (na is not null && template.SodiumPcForGypsumRec > 0 && na.Actual >= template.SodiumPcForGypsumRec)
        {
            var calciumGypsum = ca?.ProductId == 9 ? ca.Balance : 0;
            if (na.Balance + calciumGypsum < template.GypsumRecKgHa)
                Change(na, template.GypsumRecKgHa - calciumGypsum, "minimum-gypsum", "Apply template's sodium-triggered minimum combined gypsum rate.", 9, "Gypsum");
        }
        if (ph?.Actual > 7)
        {
            if (ca?.ProductId == 5) Change(ca, 0, "high-ph-remove-lime", "Remove lime above pH 7.", 0, "");
            if (mg?.ProductId == 6) Change(mg, 0, "high-ph-remove-dolomite", "Remove dolomite above pH 7.", 0, "");
        }
        // Retain the legacy order, including its second Mg conversion condition; discrepancies are reviewed separately.
        if (ca?.Actual > 75 && mg is not null) Oxide(mg, "calcium-over-75");
        if (crop.StartsWith("sugar", StringComparison.OrdinalIgnoreCase) && ca?.ProductId == 5 && mg?.ProductId == 6)
        {
            var originalCa = ca.Balance; var originalMg = mg.Balance; var reduceCa = true;
            var iterations = 0;
            while (ca.Balance + mg.Balance > 6500)
            {
                // Malformed legacy multipliers must not keep an API request in an effectively unbounded loop.
                if (++iterations > 100000) throw new InvalidOperationException("The sugar liming amount exceeds the supported calculation range. Check the template multipliers.");
                if (reduceCa) ca.Balance = Math.Max(0, ca.Balance - 500); else mg.Balance = Math.Max(0, mg.Balance - 500);
                reduceCa = !reduceCa;
            }
            Trace(trace, sampleId, "sugar-liming-cap", "Lime + Dolomite", "Alternate 500 kg/ha reductions, starting with lime, until the combined rate is at most 6500.", ca.Balance + mg.Balance,
                ("originalLime", originalCa), ("originalDolomite", originalMg), ("finalLime", ca.Balance), ("finalDolomite", mg.Balance));
        }
    }
}
