import {
  AfterViewInit, Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy,
  Output, SimpleChanges, ViewChild,
} from '@angular/core';
import * as L from 'leaflet';

/**
 * Self-contained Leaflet map. Renders OpenStreetMap tiles with a vector marker (circleMarker — no image
 * assets, so it works regardless of bundler asset paths). When [editable], clicking the map moves the marker
 * and emits (coordChange). Degrades quietly if tiles cannot load (marker + view still render).
 */
@Component({
  selector: 'app-map',
  standalone: true,
  template: `<div #host class="maphost" [style.height.px]="height"></div>`,
  styles: [`
    .maphost { width:100%; border-radius:var(--r-md, 10px); overflow:hidden; border:1px solid var(--slate-200, #e2e8f0); background:var(--slate-100, #f1f5f9); }
    :host ::ng-deep .leaflet-container { font: inherit; background:var(--slate-100, #f1f5f9); }
  `],
})
export class MapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() lat: number | null = null;
  @Input() lng: number | null = null;
  @Input() zoom = 13;
  @Input() height = 260;
  @Input() editable = false;
  @Output() coordChange = new EventEmitter<{ lat: number; lng: number }>();

  @ViewChild('host', { static: true }) host!: ElementRef<HTMLDivElement>;

  private map?: L.Map;
  private marker?: L.CircleMarker;

  ngAfterViewInit(): void {
    const center: L.LatLngExpression = [this.lat ?? 30.0444, this.lng ?? 31.2357]; // default: Cairo
    this.map = L.map(this.host.nativeElement, { attributionControl: true, zoomControl: true })
      .setView(center, this.lat != null ? this.zoom : 6);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '© OpenStreetMap contributors',
    }).addTo(this.map);

    if (this.lat != null && this.lng != null) this.setMarker(this.lat, this.lng);

    if (this.editable) {
      this.map.on('click', (e: L.LeafletMouseEvent) => {
        const { lat, lng } = e.latlng;
        this.setMarker(lat, lng);
        this.coordChange.emit({ lat: +lat.toFixed(6), lng: +lng.toFixed(6) });
      });
    }

    // The host is often laid out after creation (cards, tabs) — recalc once the frame settles.
    setTimeout(() => this.map?.invalidateSize(), 0);
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.map) return;
    if ((changes['lat'] || changes['lng']) && this.lat != null && this.lng != null) {
      this.setMarker(this.lat, this.lng);
      this.map.setView([this.lat, this.lng], this.zoom);
    }
  }

  private setMarker(lat: number, lng: number): void {
    if (!this.map) return;
    if (this.marker) {
      this.marker.setLatLng([lat, lng]);
    } else {
      this.marker = L.circleMarker([lat, lng], {
        radius: 9, color: '#1d4ed8', weight: 3, fillColor: '#3b82f6', fillOpacity: 0.85,
      }).addTo(this.map);
    }
  }

  ngOnDestroy(): void {
    this.map?.remove();
    this.map = undefined;
  }
}
