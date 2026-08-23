import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { UiService } from './core/ui.service';
import { ToastComponent } from './core/toast.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ToastComponent],
  template: '<router-outlet /><app-toast />',
})
export class AppComponent {
  // Instantiate UiService so theme/lang effects apply to <body> at startup.
  private readonly ui = inject(UiService);
}
