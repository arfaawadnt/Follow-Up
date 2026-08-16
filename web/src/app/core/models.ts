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
  governorate: string | null; city: string | null; area: string | null; encrypted: boolean;
}

export interface ContactDto { id: string; name: string; role: string; phone: string | null; birthday: string | null; }

export interface LabDetail {
  id: string; displayCode: string; name: string; segment: string; status: string;
  branch: string | null; governorate: string | null; city: string | null; area: string | null;
  category: string | null; payer: string | null; contractType: string | null;
  latitude: number | null; longitude: number | null; monthlyTarget: number;
  loyaltyPoints: number; loyaltyTier: string | null;
  collectorRepId: string | null; marketingRepId: string | null;
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
  id: string; reference: string; laboratoryId: string; labDisplayCode: string;
  category: string; status: string; stage: string; createdAt: string;
}

export interface BoardItem {
  visitId: string; laboratoryId: string; labDisplayCode: string; labName: string;
  collectorRepId: string | null; visitDate: string; scheduledTime: string;
  status: string; sampleCount: number | null; adminChecked: boolean;
}

export interface RepListItem {
  id: string; fullName: string; type: string; goalDuration: string;
  isActive: boolean; branch: string | null; governorate: string | null;
}

export interface MarketingVisit {
  id: string; laboratoryId: string; labDisplayCode: string; labName: string;
  representativeId: string; purpose: string; scheduledDate: string; status: string; outcome: string | null;
}

export interface NotificationItem { id: string; eventKey: string; title: string; body: string; createdAt: string; isRead: boolean; }
export interface RefItem { id: string; type: string; code: string; nameEn: string; nameAr: string | null; sortOrder: number; }
export interface UserListItem { id: string; username: string; roleName: string; email: string | null; isActive: boolean; isLocked: boolean; }
export interface NetworkOverview { totalLabs: number; activeLabs: number; samplesThisMonth: number; incomeThisMonth: number; }
export interface RepPerformanceRow { repId: string; repName: string; achievementPercent: number; pace: number; onTrack: boolean; salary: number; }
