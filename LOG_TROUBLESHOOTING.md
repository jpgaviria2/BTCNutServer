# Cashu Wallet Log Troubleshooting Guide

## Where to Find Logs

BTCPay Server logs are typically located in:
- **Docker**: Check container logs: `docker logs btcpayserver` or view in BTCPay Server admin panel
- **Systemd**: Usually in `/var/log/btcpayserver/` or journalctl: `journalctl -u btcpayserver -f`
- **Admin Panel**: BTCPay Server Admin → Server Settings → Logs

## Key Log Patterns to Look For

### When Receiving Ecash Tokens

Look for these log entries:

1. **Error receiving ecash:**
   ```
   (Cashu Wallet) Error receiving ecash for store {StoreId}
   ```
   - Check the exception details after this line
   - Look for stack traces

2. **Mint connection errors:**
   ```
   (Cashu) Couldn't connect to: {mint}
   ```
   - Indicates network connectivity issues with the mint
   - Check if mint URL is accessible

3. **Protocol errors:**
   ```
   (Cashu) Protocol error occurred...
   ```
   - Mint returned an error response
   - Check the error message for details

4. **Keyset errors:**
   ```
   (Cashu) Couldn't get keysets. Funds weren't spent.
   ```
   - Mint didn't return keysets
   - Could be mint connectivity or configuration issue

5. **Swap operation errors:**
   - Look for any exceptions during swap
   - Check if proofs were validated

### Common Error Scenarios

#### 1. Mint Unreachable
**Symptoms:**
- "Failed to connect to mint {mintUrl}"
- HTTP connection timeout or connection refused

**Check:**
- Can you access the mint URL from the server?
- Is there a firewall blocking connections?
- Is the mint URL correct?

#### 2. Token Decode Failure
**Symptoms:**
- "Invalid Cashu token format!"
- Token validation fails

**Check:**
- Verify token is complete and not truncated
- Check if token format matches expected (cashu://, cashuA..., cashuB...)

#### 3. Mint URL Mismatch
**Symptoms:**
- Swap fails with keyset mismatch
- Proofs not recognized by mint

**Check:**
- Token mint URL vs configured mint URL
- Token should use the mint that issued it

#### 4. Database Errors
**Symptoms:**
- Database connection errors
- Transaction rollback errors

**Check:**
- Database connectivity
- Migration status
- Table existence

## What Information to Gather

When reporting issues, include:

1. **Timestamp** of the error
2. **Full log entry** with stack trace
3. **Token mint URL** (from the token being received)
4. **Configured mint URL** (from store settings)
5. **Error message** shown to user
6. **Store ID** where error occurred

## Quick Diagnostic Commands

### Check if mint is reachable:
```bash
curl -v https://mint.minibits.cash/Bitcoin/keysets
```

### Check BTCPay Server logs for Cashu entries:
```bash
# Docker
docker logs btcpayserver 2>&1 | grep -i cashu

# Systemd
journalctl -u btcpayserver -f | grep -i cashu
```

### Check for recent errors:
```bash
docker logs btcpayserver 2>&1 | grep -i "error\|exception" | tail -50
```

## Debugging Steps

1. **Check the exact error in logs**
   - Look for "(Cashu Wallet) Error receiving ecash"
   - Note the full exception stack trace

2. **Verify mint accessibility**
   - Test mint URL from server
   - Check network connectivity

3. **Verify token format**
   - Token should start with "cashu"
   - Should decode successfully

4. **Check store configuration**
   - Verify mint is configured correctly
   - Check payment method settings

5. **Check database**
   - Verify migrations ran
   - Check table existence
   - Verify connectivity

