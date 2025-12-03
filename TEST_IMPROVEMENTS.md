# Cashu Plugin Test Coverage Improvements

## Summary
The test suite has good coverage of core functionality, but there are some gaps that could be filled to improve robustness.

## Current Test Coverage

### CashuUtilsTests ✅ Well Covered
- `GetCashuHttpClient` - ✅ Covered (valid URL, invalid URL, timeout)
- `GetTokenSatRate(CashuToken, Network)` - ✅ Covered (sat and usd tokens)
- `SimplifyToken` - ✅ Covered (valid token, multiple mints, default unit)
- `SelectProofsToSend` - ✅ Covered (exact amount, combination, bigger proof, empty, insufficient)
- `SplitToProofsAmounts` - ✅ Covered (exact keys, combination, large amount, zero)
- `SplitAmountsForPayment` - ✅ Covered (exact amount, no change, invalid inputs, amount greater than input)
- `CreateBlankOutputs` - ✅ Covered (positive amount, zero, negative, large amount)
- `CreateOutputs` - ✅ Covered (valid inputs, invalid amounts)
- `StripDleq` - ✅ Covered (with DLEQ, without DLEQ, empty list)
- `CreatePaymentRequest` - ✅ Covered (valid inputs, null mints, null/empty endpoint/invoiceId, invalid amount, amountless)
- `TryDecodeToken` - ✅ Covered (valid token, invalid token, null/empty)
- `CalculateNumberOfBlankOutputs` - ✅ Covered (positive, power of two, zero)
- `CreateProofs` - ✅ Covered (with DLEQ, without DLEQ, keyset mismatch, multiple keyset IDs)
- `FormatAmount` - ✅ Covered (Bitcoin units, special fiat units, unknown/empty unit, zero, max/min values)

### CashuWalletTests ✅ Well Covered
- Constructor variants - ✅ Covered (with/without Lightning, with/without DbContext)
- `GetKeysets` - ✅ Covered (returns all keysets, adds to DB, invalid mint, saves to DB)
- `GetActiveKeyset` - ✅ Covered (custom unit, default unit, adds to DB)
- `GetKeys` - ✅ Covered (null keysetId, specific keyset, invalid keysetId)
- `Receive/Swap` - ✅ Covered (swap with 0 fee, swap with fees, already used token, invalid keyset, invalid amounts, fee bigger than amount, invalid signature)
- `CreateMeltQuote` - ✅ Covered (valid token, without Lightning client, with keyset fees, invalid token)
- `Melt` - ✅ Covered (valid token, already spent token, invalid token, faked amount)

## Missing Test Coverage

### CashuUtils - Missing Tests

1. **`GetTokenSatRate(string mint, string unit, Network network)` overload**
   - Test with different mint URLs
   - Test with different units (sat, usd, etc.)
   - Test error handling for invalid mints
   - Test network-specific behavior

2. **`ValidateFees` (2 overloads)**
   - Test with valid fee configurations
   - Test with invalid fee configurations
   - Test edge cases (zero fees, max fees)
   - Test with different payment models

3. **`Sum` extension method**
   - Test with empty collection
   - Test with single value
   - Test with multiple values
   - Test with large values (overflow scenarios)

### CashuWallet - Missing Tests

1. **`CheckTokenState(List<Proof> proofs)`**
   - Test with valid proofs (unspent)
   - Test with spent proofs
   - Test with invalid proofs
   - Test with empty list
   - Test error handling for network issues

2. **`CheckTokenState(List<StoredProof> proofs)` overload**
   - Test with valid stored proofs
   - Test with spent stored proofs
   - Test conversion from StoredProof to Proof

3. **`RestoreProofsFromInputs`**
   - Test with valid blinded messages
   - Test with invalid blinded messages
   - Test with empty array
   - Test cancellation token handling
   - Test error handling

4. **`ValidateLightningInvoicePaid`**
   - Test with paid invoice
   - Test with unpaid invoice
   - Test with null invoiceId
   - Test with invalid invoiceId
   - Test when Lightning client is not configured

5. **`CheckMeltQuoteState`**
   - Test with valid quote ID (UNPAID state)
   - Test with valid quote ID (PAID state)
   - Test with invalid quote ID
   - Test cancellation token handling
   - Test error handling

## Integration Test Opportunities

1. **End-to-end payment flow**
   - Test complete payment flow from token receipt to invoice payment
   - Test with different payment models (SwapAndHodl, MeltImmediately)
   - Test with trusted mints vs untrusted mints

2. **Payout processor tests**
   - Test automated payout processing
   - Test payout processor configuration
   - Test payout state transitions

3. **Error recovery scenarios**
   - Test network failures during swap
   - Test mint unavailability
   - Test partial failures

## Recommendations

1. **Priority: High**
   - Add tests for `CheckTokenState` methods (critical for payout processing)
   - Add tests for `ValidateLightningInvoicePaid` (critical for MeltImmediately mode)
   - Add tests for `CheckMeltQuoteState` (important for melt flow)

2. **Priority: Medium**
   - Add tests for `RestoreProofsFromInputs` (useful for recovery scenarios)
   - Add tests for `ValidateFees` methods (important for fee validation)
   - Add tests for `GetTokenSatRate` overload (completeness)

3. **Priority: Low**
   - Add tests for `Sum` extension method (simple utility, low risk)
   - Add integration tests (time-consuming but valuable)

## Test Quality Improvements

1. **Mock external dependencies**
   - Consider mocking HTTP calls to mints for faster, more reliable tests
   - Mock Lightning client for tests that don't require real Lightning node

2. **Test data management**
   - Create test fixtures for common scenarios (valid tokens, keysets, etc.)
   - Use test data builders for complex objects

3. **Edge case coverage**
   - Add more boundary value tests
   - Test with maximum/minimum values
   - Test with malformed data

4. **Error handling**
   - Ensure all error paths are tested
   - Test exception types and messages
   - Test error recovery scenarios

