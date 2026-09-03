import { Pipe, PipeTransform } from '@angular/core';
import { ddmy } from './export.util';

/**
 * Renders any date value as dd/MM/yyyy, or dd/MM/yyyy HH:mm when passed `true`.
 * Usage: {{ visitDate | appDate }} · {{ occurredAt | appDate:true }}
 */
@Pipe({ name: 'appDate', standalone: true })
export class AppDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, withTime = false): string {
    return ddmy(value, withTime);
  }
}
