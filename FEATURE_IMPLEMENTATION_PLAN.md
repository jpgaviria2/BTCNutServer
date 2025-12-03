# BTCNutServer Feature Implementation Plan

This document outlines a structured plan for implementing features and improvements to the BTCNutServer (Cashu) BTCPay Server plugin, starting from the upstream repository baseline.

## Current State Analysis

### Existing Features
- ✅ Basic Cashu payment processing (Swap and Melt modes)
- ✅ Trusted mints configuration
- ✅ Fee configuration (Lightning fees, mint fees, customer fee advance)
- ✅ Wallet management (view balances, export tokens)
- ✅ Failed transaction recovery mechanism
- ✅ Payment via QR code and paste token
- ✅ NUT-19 payment request support
- ✅ Database persistence for proofs, mints, and failed transactions

### Current Limitations & Areas for Improvement
- Limited error handling and user feedback
- No automatic retry mechanism for failed transactions
- No payment analytics or reporting
- Limited mint validation and health checking
- No bulk operations for wallet management
- No webhook support for payment events
- Limited testing coverage
- No rate limiting or security hardening

---

## Feature Implementation Roadmap

### Phase 1: Foundation & Stability (High Priority)

#### 1.1 Enhanced Error Handling & Logging
**Priority:** High  
**Complexity:** Medium  
**Estimated Time:** 2-3 days

**Features:**
- Structured logging with correlation IDs
- User-friendly error messages in UI
- Error categorization (network, protocol, validation)
- Detailed error tracking in database
- Error notification system

**Implementation Steps:**
1. Create error categorization enum
2. Enhance exception classes with error codes
3. Add structured logging throughout payment flow
4. Update UI to display user-friendly messages
5. Add error tracking table to database

**Files to Modify:**
- `Errors/` directory - enhance exception classes
- `PaymentHandlers/CashuPaymentService.cs` - add error handling
- `Controllers/CashuControler.cs` - improve error responses
- Add new migration for error tracking

---

#### 1.2 Automatic Retry Mechanism for Failed Transactions
**Priority:** High  
**Complexity:** Medium-High  
**Estimated Time:** 3-4 days

**Features:**
- Background service for automatic retry
- Configurable retry intervals and max attempts
- Exponential backoff strategy
- Retry status tracking
- Admin notification for persistent failures

**Implementation Steps:**
1. Create `FailedTransactionRetryService` background service
2. Add retry configuration to store settings
3. Implement retry logic with exponential backoff
4. Add retry status tracking to `FailedTransaction` model
5. Create admin UI for retry management
6. Add tests for retry scenarios

**Files to Create:**
- `Services/FailedTransactionRetryService.cs`
- `ViewModels/FailedTransactionRetryViewModel.cs`
- `Views/Cashu/RetrySettings.cshtml`

**Files to Modify:**
- `Data/Models/FailedTransaction.cs` - add retry fields
- `CashuPlugin.cs` - register background service
- `PaymentHandlers/CashuPaymentService.cs` - integrate retry service

---

#### 1.3 Mint Health Monitoring & Validation
**Priority:** Medium-High  
**Complexity:** Medium  
**Estimated Time:** 2-3 days

**Features:**
- Periodic mint health checks
- Mint availability status indicator
- Automatic mint validation on configuration
- Mint response time tracking
- Health status dashboard

**Implementation Steps:**
1. Create `MintHealthService` for periodic checks
2. Add health status to `Mint` model
3. Implement health check endpoint
4. Add UI indicators for mint status
5. Create health check background service
6. Add validation on mint configuration save

**Files to Create:**
- `Services/MintHealthService.cs`
- `ViewModels/MintHealthViewModel.cs`

**Files to Modify:**
- `Data/Models/Mint.cs` - add health status fields
- `Controllers/CashuControler.cs` - add health check endpoints
- `Views/Cashu/StoreConfig.cshtml` - show mint health

---

### Phase 2: User Experience & Operations (Medium Priority)

#### 2.1 Enhanced Wallet Management
**Priority:** Medium  
**Complexity:** Medium  
**Estimated Time:** 3-4 days

**Features:**
- Bulk export operations
- Partial balance export
- Export history with filters
- Token import functionality
- Balance aggregation by mint/unit
- Export scheduling

**Implementation Steps:**
1. Add bulk export endpoint
2. Create partial export UI
3. Enhance export history view
4. Add token import functionality
5. Implement export scheduling service
6. Add balance aggregation queries

**Files to Create:**
- `Services/WalletExportService.cs`
- `ViewModels/BulkExportViewModel.cs`
- `Views/Cashu/BulkExport.cshtml`

**Files to Modify:**
- `Controllers/CashuControler.cs` - add bulk operations
- `Views/Cashu/CashuWallet.cshtml` - enhance UI
- `Data/Models/ExportedToken.cs` - add metadata

---

#### 2.2 Payment Analytics & Reporting
**Priority:** Medium  
**Complexity:** Medium-High  
**Estimated Time:** 4-5 days

**Features:**
- Payment statistics dashboard
- Revenue reports by period
- Mint usage analytics
- Fee analysis reports
- Payment method comparison
- Export reports to CSV/PDF

**Implementation Steps:**
1. Create analytics data models
2. Implement analytics service
3. Create dashboard view
4. Add charting library integration
5. Implement report generation
6. Add export functionality

**Files to Create:**
- `Services/AnalyticsService.cs`
- `ViewModels/AnalyticsViewModel.cs`
- `Views/Cashu/Analytics.cshtml`
- `Services/ReportGeneratorService.cs`

**Files to Modify:**
- `Controllers/CashuControler.cs` - add analytics endpoints
- `Data/CashuDbContext.cs` - add analytics queries

---

#### 2.3 Improved Configuration UI
**Priority:** Medium  
**Complexity:** Low-Medium  
**Estimated Time:** 2 days

**Features:**
- Step-by-step configuration wizard
- Configuration validation with real-time feedback
- Configuration presets/templates
- Configuration import/export
- Help tooltips and documentation links

**Implementation Steps:**
1. Create configuration wizard component
2. Add real-time validation
3. Create preset configurations
4. Implement import/export functionality
5. Add help documentation

**Files to Create:**
- `ViewModels/ConfigurationWizardViewModel.cs`
- `Views/Cashu/ConfigurationWizard.cshtml`
- `Services/ConfigurationValidatorService.cs`

**Files to Modify:**
- `Views/Cashu/StoreConfig.cshtml` - enhance UI
- `Controllers/CashuControler.cs` - add validation

---

### Phase 3: Advanced Features (Lower Priority)

#### 3.1 Webhook Support
**Priority:** Low-Medium  
**Complexity:** High  
**Estimated Time:** 4-5 days

**Features:**
- Webhook endpoint for payment events
- Configurable webhook URLs per store
- Webhook signature verification
- Retry mechanism for failed webhooks
- Webhook event history

**Implementation Steps:**
1. Design webhook event schema
2. Create webhook service
3. Implement signature verification
4. Add webhook configuration UI
5. Create webhook retry mechanism
6. Add webhook event logging

**Files to Create:**
- `Services/WebhookService.cs`
- `Models/WebhookEvent.cs`
- `Controllers/WebhookController.cs`
- `ViewModels/WebhookConfigurationViewModel.cs`

**Files to Modify:**
- `PaymentHandlers/CashuPaymentService.cs` - trigger webhooks
- `Data/CashuDbContext.cs` - add webhook tables

---

#### 3.2 Multi-Mint Payment Support
**Priority:** Low  
**Complexity:** High  
**Estimated Time:** 5-6 days

**Features:**
- Accept payments from multiple mints in single transaction
- Automatic mint selection based on trust/availability
- Mint priority configuration
- Cross-mint balance aggregation

**Implementation Steps:**
1. Design multi-mint payment flow
2. Implement mint selection logic
3. Update payment processing to handle multiple mints
4. Add UI for multi-mint configuration
5. Update wallet view for multi-mint balances

**Files to Create:**
- `Services/MultiMintPaymentService.cs`
- `ViewModels/MultiMintPaymentViewModel.cs`

**Files to Modify:**
- `PaymentHandlers/CashuPaymentService.cs` - major refactoring
- `Controllers/CashuControler.cs` - update endpoints

---

#### 3.3 Rate Limiting & Security Hardening
**Priority:** Medium  
**Complexity:** Medium  
**Estimated Time:** 3-4 days

**Features:**
- Rate limiting on payment endpoints
- IP-based blocking for suspicious activity
- Payment amount limits
- Token validation enhancements
- Security audit logging

**Implementation Steps:**
1. Implement rate limiting middleware
2. Add IP blocking functionality
3. Create security audit log
4. Enhance token validation
5. Add security configuration UI

**Files to Create:**
- `Middleware/RateLimitingMiddleware.cs`
- `Services/SecurityAuditService.cs`
- `Models/SecurityAuditLog.cs`

**Files to Modify:**
- `Controllers/CashuControler.cs` - add rate limiting
- `CashuAbstractions/CashuUtils.cs` - enhance validation

---

#### 3.4 API Enhancements
**Priority:** Low-Medium  
**Complexity:** Medium  
**Estimated Time:** 3-4 days

**Features:**
- RESTful API for external integrations
- API key authentication
- API documentation (Swagger/OpenAPI)
- API versioning
- Rate limiting per API key

**Implementation Steps:**
1. Design API structure
2. Implement API controllers
3. Add API key management
4. Create API documentation
5. Implement versioning

**Files to Create:**
- `Controllers/Api/CashuApiController.cs`
- `Models/ApiKey.cs`
- `Services/ApiKeyService.cs`
- `ViewModels/ApiKeyViewModel.cs`

**Files to Modify:**
- `CashuPlugin.cs` - register API services

---

### Phase 4: Testing & Quality Assurance

#### 4.1 Comprehensive Test Suite
**Priority:** High  
**Complexity:** Medium-High  
**Estimated Time:** 5-7 days

**Features:**
- Unit tests for core services
- Integration tests for payment flow
- End-to-end tests for critical paths
- Mock mint server for testing
- Test coverage reporting

**Implementation Steps:**
1. Set up test infrastructure
2. Create mock mint server
3. Write unit tests for services
4. Write integration tests
5. Set up CI/CD test pipeline
6. Generate coverage reports

**Files to Create:**
- `Tests/Unit/` - unit test files
- `Tests/Integration/` - integration test files
- `Tests/Mocks/MockMintServer.cs`

---

#### 4.2 Performance Optimization
**Priority:** Medium  
**Complexity:** Medium-High  
**Estimated Time:** 3-4 days

**Features:**
- Database query optimization
- Caching for mint keysets
- Async operation improvements
- Connection pooling
- Performance monitoring

**Implementation Steps:**
1. Profile current performance
2. Optimize database queries
3. Implement caching layer
4. Improve async operations
5. Add performance monitoring

**Files to Modify:**
- `PaymentHandlers/CashuPaymentService.cs` - optimize queries
- `CashuAbstractions/CashuWallet.cs` - add caching
- `Data/CashuDbContext.cs` - optimize queries

---

## Implementation Guidelines

### Development Workflow
1. **Create feature branch** from `main`: `git checkout -b feature/feature-name`
2. **Implement feature** following the steps outlined
3. **Write tests** for new functionality
4. **Update documentation** (README, code comments)
5. **Create pull request** with detailed description
6. **Code review** and address feedback
7. **Merge** after approval

### Code Standards
- Follow existing C# coding conventions
- Use async/await for I/O operations
- Add XML documentation comments
- Follow SOLID principles
- Write meaningful commit messages

### Testing Requirements
- All new features must have unit tests
- Critical paths must have integration tests
- Maintain minimum 70% code coverage
- Test error scenarios and edge cases

### Documentation
- Update README.md for user-facing features
- Add code comments for complex logic
- Create migration guides for breaking changes
- Document API endpoints (if applicable)

---

## Priority Matrix

| Feature | Priority | Complexity | Estimated Time | Phase |
|---------|----------|------------|----------------|-------|
| Enhanced Error Handling | High | Medium | 2-3 days | 1 |
| Automatic Retry Mechanism | High | Medium-High | 3-4 days | 1 |
| Mint Health Monitoring | Medium-High | Medium | 2-3 days | 1 |
| Enhanced Wallet Management | Medium | Medium | 3-4 days | 2 |
| Payment Analytics | Medium | Medium-High | 4-5 days | 2 |
| Improved Configuration UI | Medium | Low-Medium | 2 days | 2 |
| Webhook Support | Low-Medium | High | 4-5 days | 3 |
| Multi-Mint Payment | Low | High | 5-6 days | 3 |
| Rate Limiting & Security | Medium | Medium | 3-4 days | 3 |
| API Enhancements | Low-Medium | Medium | 3-4 days | 3 |
| Comprehensive Test Suite | High | Medium-High | 5-7 days | 4 |
| Performance Optimization | Medium | Medium-High | 3-4 days | 4 |

---

## Next Steps

1. **Review this plan** and prioritize features based on your needs
2. **Start with Phase 1** features for stability and foundation
3. **Create GitHub issues** for each feature to track progress
4. **Set up project board** to manage feature implementation
5. **Begin implementation** with the highest priority feature

---

## Notes

- This plan is a living document and should be updated as features are implemented
- Estimated times are rough estimates and may vary based on complexity
- Some features may have dependencies on others - check before starting
- Consider upstream changes and compatibility when implementing features
- Always test thoroughly before merging to main branch

---

**Last Updated:** 2025-01-XX  
**Repository:** https://github.com/d4rp4t/BTCNutServer  
**Upstream:** https://github.com/d4rp4t/BTCNutServer.git

