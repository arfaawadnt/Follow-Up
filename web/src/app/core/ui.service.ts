import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';
export type Lang = 'en' | 'ar';

/**
 * UI preferences (theme + language/direction) as signals, applied to <body> via a class + dir attribute so
 * every token-based component flips automatically (design-system mechanism). Persisted to localStorage.
 */
@Injectable({ providedIn: 'root' })
export class UiService {
  readonly theme = signal<Theme>((localStorage.getItem('followup.theme') as Theme) ?? 'light');
  readonly lang = signal<Lang>((localStorage.getItem('followup.lang') as Lang) ?? 'en');

  constructor() {
    effect(() => {
      const theme = this.theme();
      document.body.classList.toggle('dark-theme', theme === 'dark');
      localStorage.setItem('followup.theme', theme);
    });
    effect(() => {
      const lang = this.lang();
      document.body.setAttribute('dir', lang === 'ar' ? 'rtl' : 'ltr');
      document.documentElement.lang = lang;
      localStorage.setItem('followup.lang', lang);
    });
  }

  toggleTheme(): void {
    this.theme.update((t) => (t === 'light' ? 'dark' : 'light'));
  }

  toggleLang(): void {
    this.lang.update((l) => (l === 'en' ? 'ar' : 'en'));
  }
}
