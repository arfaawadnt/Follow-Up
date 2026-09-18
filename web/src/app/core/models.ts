export interface ScopeView {
  branches: string[]; governorates: string[]; cities: string[];
  areas: string[]; categories: string[]; segments: string[];
}

export interface LoginResult {
  token: string;
  expiresAt: string;
  username: string;
  roleName: string;
  privileges: string[];
  scope: ScopeView;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  truncated: boolean;
}

/** Lab picker row (GET /labs/lookup): every lab in scope, unpaged — use this for pickers, never the paged /labs list. */
export interface LabLookup { id: string; displayCode: string; name: string; area?: string | null; }
export interface LabListItem {
  id: string; displayCode: string; name: string; segment: string; status: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  category: string | null; avgMonthlySamples: number | null;
  latitude: number | null; longitude: number | null;
  collectors: string[]; collectorRepIds: string[]; marketing: string | null; encrypted: boolean; source: string;
  /** Operator-managed Credit flag; never touched by the Oracle sync. */
  credit: boolean;
  /** Display name of the lab's responsible rep (type LabResponsible); null when unassigned. */
  responsible: string | null;
}

export interface ContactDto { id: string; name: string; role: string; phone: string | null; birthday: string | null; }

export interface LabDetail {
  id: string; displayCode: string; name: string; segment: string; status: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  category: string | null; address: string | null;
  mappingCode: string | null; isEncrypted: boolean; images: string[];
  payer: string | null; contractType: string | null;
  licenseNo: string | null; licenseDate: string | null; avgMonthlySamples: number | null; preferredChannel: string | null;
  latitude: number | null; longitude: number | null; monthlyTarget: number;
  loyaltyPoints: number; loyaltyTier: string | null;
  collectorRepIds: string[]; marketingRepId: string | null;
  /** The lab's responsible rep (type LabResponsible) — the one who collects its money; null when unassigned. */
  responsibleRepId: string | null;
  workDays: string[]; visitTimes: string[]; contacts: ContactDto[]; rowVersion: number;
  /** Operator-managed Credit flag; never touched by the Oracle sync. */
  credit: boolean;
}

export interface ScheduleItem { visitId: string; labDisplayCode: string; labName: string; status: string; time: string; }
export interface UnresolvedComplaint { id: string; reference: string; labDisplayCode: string; status: string; }
export interface RepProgress { repId: string; repName: string; achievementPercent: number; onTrack: boolean; }
export interface Birthday { contactName: string; labDisplayCode: string; phone: string | null; }

export interface Dashboard {
  activeLabs: number; openComplaints: number; samplesToday: number; missedToday: number;
  todaySchedule: ScheduleItem[]; unresolvedComplaints: UnresolvedComplaint[];
  repProgress: RepProgress[]; birthdays: Birthday[];
}

export interface ComplaintListItem {
  id: string; reference: string; laboratoryId: string; labDisplayCode: string; lab: string; labCategory: string | null;
  category: string; via: string; assignedTo: string | null; description: string;
  status: string; stage: string; ageDays: number; resolution: string | null;
  resolvedAt: string | null; resolutionSummary: string | null; createdAt: string;
}

// CMP-16: server-side status breakdown for the filter pills (correct regardless of paging).
export interface ComplaintCounts { total: number; open: number; inProgress: number; resolved: number; }

export interface ComplaintDetail {
  id: string; reference: string; laboratoryId: string; labDisplayCode: string; lab: string;
  category: string; viaChannel: string; assignedTeam: string | null; details: string;
  status: string; stage: string; resolvedAt: string | null; resolvedBy: string | null;
  representativeId: string | null; representativeName: string | null; receivedAt: string | null;
  isValid: boolean | null; validityNotes: string | null; investigationNotes: string | null;
  outcomeType: string | null; outcomeSummary: string | null; resolutionSummary: string | null; createdAt: string;
}
export interface ComplaintAuditRow { occurredAt: string; actor: string; action: string; before: string | null; after: string | null; }

export interface AttachmentRef { id: string; fileName: string; contentType: string; sizeBytes: number; }

export interface BoardItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; lab: string;
  collectorRepId: string | null; rep: string | null;
  /** The lab's assigned collectors — the record dialog offers only these (falls back to all when empty). */
  collectorRepIds: string[];
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; scheduledTime: string;
  status: string; samples: number | null; markedAt: string | null; adminChecked: boolean; transferDone: boolean;
  archived?: boolean; attachments?: AttachmentRef[];
}

export interface RepListItem {
  id: string; fullName: string; type: string; goalDuration: string; goalType: string | null; metric: string | null;
  target: number; salary: number; phone: string | null; assignedCount: number;
  isActive: boolean; branch: string | null; governorate: string | null; city: string | null; area: string | null;
  employmentType: string | null; appointedOn: string | null; source: string;
}

export interface RepDetail {
  id: string; fullName: string; type: string; goalDuration: string; goalType: string | null; metric: string | null;
  salary: number; target: number; phone: string | null; branch: string | null; governorate: string | null;
  city: string | null; area: string | null; employmentType: string | null; appointedOn: string | null;
  isActive: boolean; rowVersion: number;
}

export interface MarketingVisit {
  id: string; reference: string; laboratoryId: string; labDisplayCode: string; lab: string; area: string | null; governorate: string | null;
  representativeId: string; rep: string | null; purpose: string; scheduledDate: string; scheduledTime: string | null;
  plan: string | null; status: string; outcome: string | null;
}

export interface TransferItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labName: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; visitTime: string; collectorName: string | null; samples: number | null;
  transferDone: boolean; driverName: string | null; driverMobile: string | null; carPlate: string | null;
  transferRepId: string | null; transferRepName: string | null; transferTime: string | null;
  archived?: boolean; attachments?: AttachmentRef[];
}
export interface ReceivingItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labName: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; visitTime: string; collectorName: string | null; samples: number | null; status: string;
  transferRepName: string | null; transferTime: string | null; receivedTime: string | null;
  archived?: boolean; attachments?: AttachmentRef[];
}
export interface SampleTracking {
  id: string; area: string; date: string; count: number;
  dataEntryBy: string | null; dataEntryAt: string | null;
  reviewBy: string | null; reviewAt: string | null;
  sortBy: string | null; sortAt: string | null; notes: string | null; isComplete: boolean;
}

export interface SampleLifecycleRow {
  lab: string; labDisplayCode: string; area: string | null; visitDate: string; visitTime: string; samples: number | null;
  collectorName: string | null; collectedAt: string | null;
  transferRepName: string | null; driverName: string | null; driverMobile: string | null; carPlate: string | null;
  transferredAt: string | null; receivedAt: string | null;
  dataEntryBy: string | null; dataEntryAt: string | null;
  reviewBy: string | null; reviewAt: string | null;
  sortBy: string | null; sortAt: string | null; notes: string | null;
  visitId?: string | null; attachments?: AttachmentRef[];
}

export interface OutsourceTest {
  id?: string; testCode: string; testName: string; sampleVolume: string;
  testFees: number; outsourceFees: number; netRevenue?: number;
}
export interface OutsourceSample {
  id: string; laboratoryId: string; labDisplayCode: string; labName: string;
  visitDate: string; destinationLab: string | null; quantity: number; status: string; notes: string | null;
  tests?: OutsourceTest[];
}
export interface TestLookup { code: string; name: string; testType: number; }
export interface OutsourceTrackingRow {
  visitDate: string; laboratoryId: string; labDisplayCode: string; labName: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  testCode: string; testName: string; sampleVolume: string;
  testFees: number; outsourceFees: number; netRevenue: number;
}

export interface UserLookup { id: string; username: string; }

export interface NotificationItem { id: string; eventKey: string; title: string; body: string; createdAt: string; isRead: boolean; }
export interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; sortOrder: number; }
export interface UserListItem {
  id: string; username: string; displayName: string | null; roleName: string; language: string;
  privilegeCount: number; email: string | null; isActive: boolean; isLocked: boolean;
}
export interface RoleScope {
  branches: string[]; governorates: string[]; cities: string[]; areas: string[]; categories: string[]; segments: string[];
}
export interface RoleItem {
  id: string; name: string; privileges: string[]; defaultLanguage: string; defaultTheme: string;
  isBuiltIn: boolean; scope: RoleScope;
}
export interface NetworkOverview { totalLabs: number; activeLabs: number; samplesThisMonth: number; incomeThisMonth: number; }

export interface SettingDto { key: string; value: string | null; isSecret: boolean; }
export interface RetentionDto { days: number | null; enabled: boolean; }
export interface LoyaltyLedger { laboratoryId: string; code: string; name: string; branch: string | null; city: string | null; monthlyTarget: number; mtdSamples: number; loyaltyPoints: number; loyaltyTier: string | null; }
export interface Commission { repId: string; name: string; type: string; goalType: string; period: number; targetAmount: number; achievedAmount: number; baseSalary: number; commissionEarned: number; bonusEarned: number; totalPayout: number; isLocked: boolean; }
export interface LabStat {
  date: string; labCode: string; name: string | null; category: string | null; segment: string | null;
  governorate: string | null; city: string | null; area: string | null; branch: string | null; status: string | null;
  registrations: number; testCount: number; income: number;
  /** Registration branch (where the registrations were made); null for rows synced before the split or imported from xlsx. */
  regBranch: string | null;
}
export interface RepPerformanceRow {
  repId: string; name: string; type: string; goalType: string; metric: string | null; goalDuration: string;
  target: number; achieved: number; pct: number; paceLabel: string; onTrack: boolean; salary: number;
}

// ---- Accounting module ----
export interface TreasuryReasonDto { id: string; name: string; isActive: boolean; }
/** A treasury as the caller sees it: only View-granted treasuries are listed, with the caller's own rights on each. */
export interface TreasuryDto { id: string; name: string; branches: string[]; isActive: boolean; canValidate: boolean; canUpdate: boolean; }
export interface TreasuryEntryDto {
  id: string; serial: number; date: string; treasuryId: string; treasuryName: string;
  debit: number; credit: number; reasonId: string | null; reasonName: string; notes: string | null;
  /** Manual | AutoCollection (mirrors a Collection's cash). */
  origin: string;
  /** NotRequired (manual) | Pending | Validated. */
  validationStatus: string;
  collectionId: string | null; collectedCash: number | null; systemNote: string | null;
  validatedAt: string | null; validatedBy: string | null; validationNote: string | null;
}
export interface CollectionTreasurySyncResult { linked: number; unplaced: number; }
/** A role's rights on one treasury (Roles page). */
export interface TreasuryGrant { treasuryId: string; treasuryName: string; isActive: boolean; canView: boolean; canValidate: boolean; canUpdate: boolean; }
export interface PenaltyDto {
  id: string; serial: number; date: string; laboratoryId: string; labDisplayCode: string; labName: string;
  accNo: string; patientName: string; wrongTestCode: string | null; wrongTestName: string | null; wrongValue: number;
  rightTestCode: string | null; rightTestName: string | null; rightValue: number;
  /** right − wrong for every user type: on the rep statement the right test is a debit, the wrong test a credit. */
  penalty: number;
  /** Who made the error: the kind (Rep / DataEntry / Technician / LabRequest) plus the resolved person. */
  userType: string; performedById: string | null; performedByName: string | null;
  /** The lab's Lab Responsible — the statement a DataEntry / Technician / LabRequest penalty posts to. */
  responsibleRepId: string | null; responsibleRepName: string | null;
  /** LDM validation of the Acc No against the synced registration lines: Valid | AccNotFound | LabMismatch | TestMissing. */
  ldmStatus: string; ldmNote: string | null;
}
/** A person a penalty can be attributed to (GET /accounting/penalty-actors?userType=…); `detail` = rep type for reps. */
export interface PenaltyActorDto { id: string; name: string; detail: string | null; }
export interface DeductionDto {
  id: string; serial: number; date: string; areaId: string; areaName: string; reason: string; value: number;
  notes: string | null; periodFrom: string | null; periodTo: string | null;
  /** Manual | AutoDeal (the month's automated Percentage Deal row). */
  origin: string;
  /** An automated row whose value an operator changed — the automation leaves it alone. */
  isAdjusted: boolean;
  /** System-written details: the deal calculation basis. */
  systemNote: string | null;
}
export interface DeductionSuggestion { value: number; basis: string; }
export interface DeductionAutomationResult {
  month: string; through: string; dealAreas: number; dealCreated: number; dealRecalculated: number; dealSkippedAdjusted: number;
  collectionsLinked: number; collectionsUnplaced: number;
}
/** One rep's part of a collection (the amount that rep handed in); for a Single collection it is the total. */
export interface CollectionShare { repId: string; repName: string; amount: number; }
/** A collection belongs to its reps (a Lab Responsible collects from many labs) — there is no lab on it. */
export interface CollectionDto {
  id: string; serial: number; date: string; type: string; shares: CollectionShare[];
  repIds: string[]; repNames: string[]; cash: number; bank: number; total: number;
  iban: string | null; doneBy: string | null; notes: string | null;
}
export interface RepStatementRow { date: string; kind: string; debit: number; credit: number; notes: string | null; balance: number; sourceId: string | null; }
export interface RepStatement { representativeId: string; repName: string; rows: RepStatementRow[]; totalDebit: number; totalCredit: number; balance: number; }
/** Statement by dimension (Responsible | Area | Lab). */
export interface Statement { by: string; subjectId: string; subjectName: string; rows: RepStatementRow[]; totalDebit: number; totalCredit: number; balance: number; }
/** Real-income sheet (Rep Statement page): a Lab Responsible's per-lab entries for one area and date. */
export interface RealIncomeRep { id: string; fullName: string; labCount: number; }
export interface RealIncomeLab { id: string; displayCode: string; name: string; }
export interface RealIncomeRow {
  laboratoryId: string; labDisplayCode: string; labName: string; hasVisit: boolean; visitTotalRequired: number | null; visitSamples: number | null;
  ldmIncome: number; penalty: number; previousRemaining: number;
  entryId: string | null; samples: number; totalRequired: number; paid: number; remaining: number; delayedPayment: number; notes: string | null;
}
export interface RealIncomeSheet { date: string; areaId: string; areaName: string; representativeId: string; repName: string; rows: RealIncomeRow[]; }

// ---- Inventory module ----
export interface ManufacturerDto { id: string; name: string; country: string | null; notes: string | null; isActive: boolean; itemCount: number; }
export interface SupplierDto { id: string; name: string; contactPerson: string | null; phone: string | null; email: string | null; address: string | null; notes: string | null; isActive: boolean; openOrders: number; }
export interface StoreDto { id: string; name: string; branch: string; location: string | null; isActive: boolean; lotCount: number; stockValue: number; }
export interface ItemTestLink { testCode: string; testType: number; testName: string; quantityPerTest: number; }
export interface InventoryItemDto {
  id: string; code: string; name: string; kind: string; manufacturerId: string; manufacturerName: string; catalogNumber: string | null;
  unit: string; minStock: number; reorderQuantity: number; expiryWarningDays: number; storageConditions: string | null; notes: string | null; isActive: boolean;
  /** Total on hand over the caller's visible stores. */
  onHand: number; testLinks: ItemTestLink[];
}
/** Stock of one item summed over the visible (or the selected) store(s), with the alert flags. */
export interface StockRow {
  itemId: string; code: string; name: string; kind: string; unit: string; manufacturerName: string; minStock: number; reorderQuantity: number;
  onHand: number; value: number; lotCount: number; expiringQuantity: number; expiredQuantity: number; nearestExpiry: string | null; isLow: boolean; isOut: boolean;
}
export interface StockLotDto {
  id: string; itemId: string; itemCode: string; itemName: string; unit: string; storeId: string; storeName: string; lotNumber: string;
  expiryDate: string | null; quantity: number; unitCost: number; value: number; firstReceivedOn: string;
  /** Ok | Expiring | Expired */
  status: string;
}
/** kind: LowStock | OutOfStock | Expiring | Expired */
export interface InventoryAlert {
  kind: string; itemId: string; itemCode: string; itemName: string; unit: string; storeId: string | null; storeName: string | null;
  lotId: string | null; lotNumber: string | null; expiryDate: string | null; quantity: number; threshold: number | null; message: string;
}
export interface InventoryDashboard {
  activeItems: number; stores: number; lotsWithStock: number; stockValue: number; lowStock: number; outOfStock: number; expiring: number; expired: number;
  openPurchaseOrders: number; transfersInTransit: number; alerts: InventoryAlert[];
}
export interface InventoryAlertResult { lowStock: number; outOfStock: number; expiring: number; expired: number; recipients: number; }
export interface PurchaseOrderLine {
  id: string; itemId: string; itemCode: string; itemName: string; unit: string; orderedQuantity: number; unitPrice: number;
  receivedQuantity: number; outstanding: number; lineTotal: number; notes: string | null;
}
export interface GoodsReceiptLine { id: string; orderLineId: string; itemId: string; itemCode: string; itemName: string; unit: string; quantity: number; lotNumber: string; expiryDate: string | null; unitCost: number; }
export interface GoodsReceiptDto {
  id: string; serial: number; number: string; purchaseOrderId: string; purchaseOrderNumber: string; storeId: string; storeName: string; supplierName: string;
  receivedDate: string; deliveryNote: string | null; invoiceNumber: string | null; notes: string | null; receivedBy: string; lines: GoodsReceiptLine[];
}
export interface PurchaseOrderDto {
  id: string; serial: number; number: string; supplierId: string; supplierName: string; storeId: string; storeName: string; orderDate: string; expectedDate: string | null;
  /** Draft | Ordered | PartiallyReceived | Received | Closed | Cancelled */
  status: string; reference: string | null; notes: string | null; orderedOn: string | null; closedOn: string | null; total: number;
  orderedQuantity: number; receivedQuantity: number; createdBy: string; lines: PurchaseOrderLine[]; receipts: GoodsReceiptDto[];
}
export interface StockMovementDto {
  id: string; serial: number; date: string; createdAt: string; type: string; itemId: string; itemCode: string; itemName: string; unit: string;
  storeId: string; storeName: string; lotId: string; lotNumber: string; expiryDate: string | null; quantity: number; balanceAfter: number; unitCost: number;
  referenceKind: string; referenceId: string | null; referenceNumber: string | null; reason: string | null; testCode: string | null; notes: string | null; performedBy: string;
}
export interface StockTransferLine { id: string; itemId: string; itemCode: string; itemName: string; unit: string; sourceLotId: string; lotNumber: string; expiryDate: string | null; quantity: number; receivedQuantity: number | null; unitCost: number; }
export interface StockTransferDto {
  id: string; serial: number; number: string; fromStoreId: string; fromStoreName: string; toStoreId: string; toStoreName: string; date: string;
  /** InTransit | Received | Cancelled */
  status: string; notes: string | null; receivedDate: string | null; receiveNotes: string | null; createdBy: string; canReceive: boolean; lines: StockTransferLine[];
}
export interface UtilizationTest { testCode: string; testType: number; testName: string; testCount: number; quantityPerTest: number; expected: number; }
export interface UtilizationRow { itemId: string; itemCode: string; itemName: string; unit: string; testsPerformed: number; expected: number; actual: number; variance: number; utilizationPct: number | null; tests: UtilizationTest[]; }
