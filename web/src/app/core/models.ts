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

export interface LabListItem {
  id: string; displayCode: string; name: string; segment: string; status: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  category: string | null; avgMonthlySamples: number | null;
  latitude: number | null; longitude: number | null;
  collectors: string[]; marketing: string | null; encrypted: boolean;
}

export interface ContactDto { id: string; name: string; role: string; phone: string | null; birthday: string | null; }

export interface LabDetail {
  id: string; displayCode: string; name: string; segment: string; status: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  category: string | null; payer: string | null; contractType: string | null;
  licenseNo: string | null; licenseDate: string | null; avgMonthlySamples: number | null; preferredChannel: string | null;
  latitude: number | null; longitude: number | null; monthlyTarget: number;
  loyaltyPoints: number; loyaltyTier: string | null;
  collectorRepIds: string[]; marketingRepId: string | null;
  workDays: string[]; visitTimes: string[]; contacts: ContactDto[]; rowVersion: number;
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
  id: string; reference: string; laboratoryId: string; labDisplayCode: string; lab: string;
  category: string; via: string; assignedTo: string | null; description: string;
  status: string; stage: string; ageDays: number; resolution: string | null; createdAt: string;
}
export interface ComplaintAuditRow { occurredAt: string; actor: string; action: string; before: string | null; after: string | null; }

export interface BoardItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labCode: string; lab: string;
  collectorRepId: string | null; rep: string | null;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; scheduledTime: string;
  status: string; samples: number | null; adminChecked: boolean; transferDone: boolean;
}

export interface RepListItem {
  id: string; fullName: string; type: string; goalDuration: string; goalType: string | null; metric: string | null;
  target: number; salary: number; phone: string | null; assignedCount: number;
  isActive: boolean; branch: string | null; governorate: string | null; area: string | null; employmentType: string | null;
}

export interface RepDetail {
  id: string; fullName: string; type: string; goalDuration: string; goalType: string | null; metric: string | null;
  salary: number; target: number; phone: string | null; branch: string | null; governorate: string | null;
  area: string | null; employmentType: string | null; isActive: boolean; rowVersion: number;
}

export interface MarketingVisit {
  id: string; laboratoryId: string; labDisplayCode: string; lab: string; area: string | null; governorate: string | null;
  representativeId: string; rep: string | null; purpose: string; scheduledDate: string; status: string; outcome: string | null;
}

export interface TransferItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labCode: string; labName: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; visitTime: string; collectorName: string | null; samples: number | null;
  transferDone: boolean; driverName: string | null; driverMobile: string | null; carPlate: string | null;
  transferRepId: string | null; transferRepName: string | null; transferTime: string | null;
}
export interface ReceivingItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labCode: string; labName: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  visitDate: string; visitTime: string; samples: number | null; status: string;
  transferRepName: string | null; receivedTime: string | null;
}
export interface SampleTracking {
  id: string; area: string; date: string; count: number;
  dataEntryBy: string | null; dataEntryAt: string | null;
  reviewBy: string | null; reviewAt: string | null;
  sortBy: string | null; sortAt: string | null; isComplete: boolean;
}

export interface NotificationItem { id: string; eventKey: string; title: string; body: string; createdAt: string; isRead: boolean; }
export interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; sortOrder: number; }
export interface UserListItem { id: string; username: string; roleName: string; email: string | null; isActive: boolean; isLocked: boolean; }
export interface NetworkOverview { totalLabs: number; activeLabs: number; samplesThisMonth: number; incomeThisMonth: number; }

export interface SettingDto { key: string; value: string | null; isSecret: boolean; }
export interface RetentionDto { days: number | null; enabled: boolean; }
export interface LoyaltyLedger { laboratoryId: string; code: string; name: string; branch: string | null; city: string | null; monthlyTarget: number; mtdSamples: number; loyaltyPoints: number; loyaltyTier: string | null; }
export interface Commission { repId: string; name: string; type: string; goalType: string; period: number; targetAmount: number; achievedAmount: number; baseSalary: number; commissionEarned: number; bonusEarned: number; totalPayout: number; isLocked: boolean; }
export interface LabStat { date: string; labCode: string; registrations: number; testCount: number; income: number; }
export interface RepPerformanceRow { repId: string; repName: string; achievementPercent: number; pace: number; onTrack: boolean; salary: number; }
