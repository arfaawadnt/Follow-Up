import { Injectable, Pipe, PipeTransform, inject } from '@angular/core';
import { UiService } from './ui.service';

type Dict = Record<string, string>;

const EN: Dict = {
  'app.title': 'Follow-Up · Mega Laboratory',
  'nav.dashboard': 'Dashboard', 'nav.labs': 'Laboratories', 'nav.reps': 'Representatives',
  'nav.daily': 'Daily Board', 'nav.transfers': 'Transfers', 'nav.marketing': 'Marketing',
  'nav.complaints': 'Complaints', 'nav.reports': 'Reports', 'nav.notifications': 'Notifications',
  'nav.users': 'Users', 'nav.setup': 'Setup',
  'action.signout': 'Sign out', 'action.signin': 'Sign in', 'action.save': 'Save', 'action.cancel': 'Cancel',
  'action.create': 'Create', 'action.search': 'Search', 'action.checkin': 'Check in', 'action.miss': 'Miss',
  'action.verify': 'Verify', 'action.retry': 'Retry',
  'common.loading': 'Loading…', 'common.empty': 'Nothing to show.', 'common.total': 'total',
  'common.username': 'Username', 'common.password': 'Password', 'common.required': 'required',
  'login.subtitle': 'Laboratory Marketing Platform', 'login.error': 'Sign-in failed. Check your credentials.',
  'kpi.activeLabs': 'Active labs', 'kpi.openComplaints': 'Open complaints', 'kpi.samplesToday': 'Samples today', 'kpi.missedToday': 'Missed today',
  'dash.schedule': "Today's schedule", 'dash.unresolved': 'Unresolved complaints',
  'labs.title': 'Laboratories', 'labs.new': 'New laboratory', 'labs.code': 'Code', 'labs.name': 'Name',
  'labs.segment': 'Segment', 'labs.governorate': 'Governorate', 'labs.status': 'Status',
  'daily.title': 'Daily Board', 'daily.samples': 'Samples', 'daily.time': 'Time',
  'marketing.title': 'Marketing Visits', 'reps.title': 'Representatives', 'reports.title': 'Reports',
  'reports.overview': 'Network overview', 'reports.performance': 'Rep performance',
  'notifications.title': 'Notifications', 'setup.title': 'Reference Data', 'users.title': 'Users',
};

const AR: Dict = {
  'app.title': 'المتابعة · معمل ميجا',
  'nav.dashboard': 'لوحة القيادة', 'nav.labs': 'المعامل', 'nav.reps': 'المناديب',
  'nav.daily': 'لوحة اليوم', 'nav.transfers': 'التحويلات', 'nav.marketing': 'التسويق',
  'nav.complaints': 'الشكاوى', 'nav.reports': 'التقارير', 'nav.notifications': 'الإشعارات',
  'nav.users': 'المستخدمون', 'nav.setup': 'الإعدادات',
  'action.signout': 'تسجيل الخروج', 'action.signin': 'تسجيل الدخول', 'action.save': 'حفظ', 'action.cancel': 'إلغاء',
  'action.create': 'إنشاء', 'action.search': 'بحث', 'action.checkin': 'تسجيل الزيارة', 'action.miss': 'زيارة فائتة',
  'action.verify': 'تأكيد', 'action.retry': 'إعادة المحاولة',
  'common.loading': 'جارٍ التحميل…', 'common.empty': 'لا يوجد ما يعرض.', 'common.total': 'الإجمالي',
  'common.username': 'اسم المستخدم', 'common.password': 'كلمة المرور', 'common.required': 'مطلوب',
  'login.subtitle': 'منصة تسويق المعامل', 'login.error': 'فشل تسجيل الدخول. تحقق من بياناتك.',
  'kpi.activeLabs': 'المعامل النشطة', 'kpi.openComplaints': 'الشكاوى المفتوحة', 'kpi.samplesToday': 'عينات اليوم', 'kpi.missedToday': 'الزيارات الفائتة اليوم',
  'dash.schedule': 'جدول اليوم', 'dash.unresolved': 'الشكاوى غير المحلولة',
  'labs.title': 'المعامل', 'labs.new': 'معمل جديد', 'labs.code': 'الكود', 'labs.name': 'الاسم',
  'labs.segment': 'الشريحة', 'labs.governorate': 'المحافظة', 'labs.status': 'الحالة',
  'daily.title': 'لوحة اليوم', 'daily.samples': 'العينات', 'daily.time': 'الوقت',
  'marketing.title': 'الزيارات التسويقية', 'reps.title': 'المناديب', 'reports.title': 'التقارير',
  'reports.overview': 'نظرة عامة على الشبكة', 'reports.performance': 'أداء المناديب',
  'notifications.title': 'الإشعارات', 'setup.title': 'البيانات المرجعية', 'users.title': 'المستخدمون',
};

const DICTS: Record<string, Dict> = { en: EN, ar: AR };

/** Minimal bilingual (EN/AR) translation lookup driven by the UiService language signal. */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly ui = inject(UiService);
  t(key: string): string {
    return DICTS[this.ui.lang()]?.[key] ?? DICTS['en'][key] ?? key;
  }
}

/** Impure so it re-evaluates when the language toggles (small app, cheap). Usage: {{ 'nav.labs' | t }} */
@Pipe({ name: 't', standalone: true, pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(key: string): string {
    return this.i18n.t(key);
  }
}
