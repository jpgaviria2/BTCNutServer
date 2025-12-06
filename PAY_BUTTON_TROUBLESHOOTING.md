# Pay Button Troubleshooting Guide

## Issue
The Pay button on checkout pages was working before but is not working now.

## Findings from Server Logs

### Critical Issue Found
1. **Missing Database Table**: The `WalletTransactions` table doesn't exist
   - Error: `relation "BTCPayServer.Plugins.Cashu.WalletTransactions" does not exist`
   - This is causing the wallet page to crash
   - Migration may not be running on startup

2. **Payment Processing Logs**
   - Payment attempts are being logged: `(Cashu) Processing payment for invoice Viu2rK9Wrio7mMD3cN31us`
   - Amount shows as `0 sat` which is suspicious
   - No error logs after the initial processing log

## Fixes Applied

1. **MigrationRunner Improvements**
   - Added logging to track when migrations run
   - Added error handling with detailed logging
   - Location: `Plugin/BTCPayServer.Plugins.Cashu/Data/MigrationRunner.cs`

2. **Plugin Rebuilt**
   - New plugin file: `dist/btcnutserver-test/0.0.1.0/btcnutserver-test.btcpay`
   - Size: 2.1M
   - Includes migration improvements

## Steps to Fix

1. **Upload the rebuilt plugin** to your BTCPay Server
2. **Restart BTCPay Server** to trigger migrations
3. **Check logs** for migration messages:
   ```
   Starting Cashu plugin database migrations...
   Cashu plugin database migrations completed successfully
   ```
4. **Test the Pay button** again

## If Issues Persist

### Check Server Logs
```bash
docker logs generated_btcpayserver_1 2>&1 | grep -iE "(cashu|payment|error|exception)" | tail -50
```

### Check Browser Console
- Open browser developer tools (F12)
- Go to Console tab
- Look for JavaScript errors
- Check Network tab for failed requests

### Verify Invoice Status
- Check if the invoice is already paid
- Verify the invoice amount is correct
- Ensure Cashu payment method is enabled for the invoice

### Common Issues

1. **Invoice Already Paid**
   - If an invoice is already paid, the amount will be 0
   - Try creating a new invoice

2. **Cashu Payment Method Not Enabled**
   - Check store settings
   - Ensure Cashu is enabled for the invoice

3. **Mint Connection Issues**
   - Check if the mint URL is accessible
   - Verify mint is responding

4. **Token Issues**
   - Ensure the token is valid
   - Check token format matches expected version

## Payment Flow

The Pay button uses the `PayByToken` endpoint:
- Route: `POST ~/cashu/PayInvoice`
- Parameters: `token` (Cashu token), `invoiceId`
- This endpoint calls `CashuPaymentService.ProcessPaymentAsync()`

If there are errors, they should be returned as `BadRequest` with error messages.

## Code Locations

- **PayByToken Endpoint**: `Plugin/BTCPayServer.Plugins.Cashu/Controllers/CashuControler.cs` line 673
- **Payment Service**: `Plugin/BTCPayServer.Plugins.Cashu/PaymentHandlers/CashuPaymentService.cs`
- **Migration Runner**: `Plugin/BTCPayServer.Plugins.Cashu/Data/MigrationRunner.cs`

