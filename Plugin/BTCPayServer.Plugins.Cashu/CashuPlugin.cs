using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.Payouts.Cashu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.Cashu;

public class CashuPlugin : BaseBTCPayServerPlugin
{
    public const string PluginNavKey = nameof(CashuPlugin) + "Nav";
    public override string Identifier => "btcnutserver-test";
    public override string Name => "BTCNutServer";
    public override string Description => "Enables trustless Cashu eCash payments in BTCPay Server. Early beta, don't be reckless.";

    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    };

    internal static PaymentMethodId CashuPmid = new PaymentMethodId("CASHU");
    internal static string CashuDisplayName = "Cashu";

    public override void Execute(IServiceCollection services)
    {
        services.AddTransactionLinkProvider(CashuPmid, new CashuTransactionLinkProvider("cashu"));

        services.AddSingleton(provider => 
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPaymentMethodHandler)));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(CashuCheckoutModelExtension)));
        
        // Payment Link Extension Registration
        services.AddSingleton<PaymentHandlers.CashuPaymentLinkExtension>();
        services.AddSingleton<IPaymentLinkExtension>(provider => 
            provider.GetRequiredService<PaymentHandlers.CashuPaymentLinkExtension>());
        
        // Cheat Mode Extension Registration (for development/testing)
        services.AddSingleton<PaymentHandlers.CashuCheckoutCheatModeExtension>();
        services.AddSingleton<ICheckoutCheatModeExtension>(provider => 
            provider.GetRequiredService<PaymentHandlers.CashuCheckoutCheatModeExtension>());
        
        services.AddDefaultPrettyName(CashuPmid, CashuDisplayName);
        
        //Cashu Singletons
        services.AddSingleton<CashuStatusProvider>();
        services.AddSingleton<CashuPaymentService>();
        
        // Payout Handler Registration
        services.AddSingleton(provider =>
            (IPayoutHandler)ActivatorUtilities.CreateInstance(provider, typeof(CashuPayoutHandler)));
        
        // Payout Processor Registration
        services.AddSingleton<CashuAutomatedPayoutSenderFactory>();
        services.AddSingleton<BTCPayServer.PayoutProcessors.IPayoutProcessorFactory>(provider => 
            provider.GetRequiredService<CashuAutomatedPayoutSenderFactory>());
        
        // Mint State Monitor (for background payout checking)
        services.AddSingleton<Services.CashuMintStateMonitor>();
        services.AddHostedService<Services.CashuMintStateMonitor>(provider => 
            provider.GetRequiredService<Services.CashuMintStateMonitor>());
        
        //Ui extensions
        services.AddUIExtension("store-wallets-nav", "CashuStoreNav");
        services.AddUIExtension("checkout-payment", "CashuCheckout");

        //Database Services
        services.AddSingleton<CashuDbContextFactory>();
        services.AddDbContext<CashuDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<CashuDbContextFactory>();
            factory.ConfigureBuilder(o);
        });
        services.AddHostedService<MigrationRunner>();
            
        base.Execute(services);
    }
}