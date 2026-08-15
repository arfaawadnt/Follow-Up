import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { UiService } from './core/ui.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: '<router-outlet />',
})
export class AppComponent {
  // Instantiate UiService so theme/lang effects apply to <body> at startup.
  private readonly ui = inject(UiService);
}
