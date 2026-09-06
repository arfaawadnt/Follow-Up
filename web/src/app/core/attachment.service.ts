import { Injectable, inject } from '@angular/core';
import { ApiService } from './api.service';
import { ToastService } from './toast.service';
import { AttachmentRef } from './models';

/** Shared upload + authenticated-view of visit documents, reused by the daily record dialog, the manual-record
 *  dialog, and the view links on Daily / Transfers / Lab Check-in / Sample Tracking. */
@Injectable({ providedIn: 'root' })
export class AttachmentService {
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  /** Uploads one file (multipart) and returns the pending attachment reference to bind on record. */
  upload(file: File) {
    const data = new FormData();
    data.append('file', file);
    return this.api.post<AttachmentRef>('/daily/upload', data);
  }

  /** Fetches the attachment as an authenticated blob (token via interceptor) and opens it in a new tab. */
  view(id: string): void {
    this.api.getBlob(`/daily/attachments/${id}`).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const win = window.open(url, '_blank');
        if (!win) this.toast.warning('Allow pop-ups to view the document.');
        setTimeout(() => URL.revokeObjectURL(url), 60000);
      },
      error: () => this.toast.warning('Could not open the document.'),
    });
  }
}
