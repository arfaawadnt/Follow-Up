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
