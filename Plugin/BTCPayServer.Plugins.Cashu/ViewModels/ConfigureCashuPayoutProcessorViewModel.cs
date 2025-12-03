using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Plugins.Cashu.Payouts.Cashu;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class ConfigureCashuPayoutProcessorViewModel
{
    public ConfigureCashuPayoutProcessorViewModel()
    {
        IntervalMinutes = AutomatedPayoutConstants.DefaultIntervalMinutes;
    }

    public ConfigureCashuPayoutProcessorViewModel(CashuAutomatedPayoutBlob blob)
    {
        IntervalMinutes = blob.Interval.TotalMinutes;
        ProcessNewPayoutsInstantly = blob.ProcessNewPayoutsInstantly;
    }

    [Display(Name = "Process approved payouts instantly")]
    public bool ProcessNewPayoutsInstantly { get; set; }

    [Range(AutomatedPayoutConstants.MinIntervalMinutes, AutomatedPayoutConstants.MaxIntervalMinutes)]
    public double IntervalMinutes { get; set; }

    public CashuAutomatedPayoutBlob ToBlob()
    {
        return new CashuAutomatedPayoutBlob
        {
            ProcessNewPayoutsInstantly = ProcessNewPayoutsInstantly,
            Interval = TimeSpan.FromMinutes(IntervalMinutes),
        };
    }
}

