import { Component, EventEmitter, Output } from '@angular/core';
import { Subscription } from 'rxjs';
import { PcapUploadService } from '../pcap-upload.service';
import { AnalysisResponse, ApiProblemDetails } from '../analysis-response.model';

export type UploadState = 'idle' | 'uploading' | 'success' | 'empty' | 'partial' | 'error';

@Component({
  selector: 'app-pcap-upload',
  imports: [],
  templateUrl: './pcap-upload.component.html',
  styleUrls: ['./pcap-upload.component.css'],
})
export class PcapUploadComponent {
  constructor(private pcapUploadService: PcapUploadService) {}

  selectedFile: File | null = null;
  state: UploadState = 'idle';
  errorMessage: string | null = null;

  /** Emits the real API result (or null when the view should show an error/empty state). */
  @Output() resultChanged = new EventEmitter<AnalysisResponse | null>();

  private currentUpload?: Subscription;
  private requestSequence = 0;

  get isBusy(): boolean {
    return this.state === 'uploading';
  }

  onPcapFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;

    if (!input.files || input.files.length === 0) {
      return;
    }

    // Duplicate-submission prevention: ignore new selections while an upload is in flight.
    if (this.isBusy) {
      return;
    }

    this.selectedFile = input.files[0];
    this.startUpload(this.selectedFile);
  }

  cancelUpload(): void {
    this.currentUpload?.unsubscribe();
    this.currentUpload = undefined;
    this.state = 'idle';
  }

  private startUpload(file: File): void {
    const formData = new FormData();
    formData.append('file', file);

    this.state = 'uploading';
    this.errorMessage = null;

    // Stale-response protection: only the most recent request's result is applied.
    const sequence = ++this.requestSequence;

    this.currentUpload = this.pcapUploadService.onUploadPcapFile(formData).subscribe({
      next: (response) => {
        if (sequence !== this.requestSequence) {
          return; // A newer upload superseded this one.
        }
        this.applyResult(response);
      },
      error: (error) => {
        if (sequence !== this.requestSequence) {
          return;
        }
        this.state = 'error';
        const problem: ApiProblemDetails | undefined = error?.error;
        this.errorMessage = problem?.detail ?? problem?.title ?? 'Upload or analysis failed.';
        this.resultChanged.emit(null);
      },
    });
  }

  private applyResult(response: AnalysisResponse): void {
    const hasFindings = response.conversations.length > 0 || response.unresolvedObservations.length > 0;
    const isPartial = !response.reachedEndOfFile || response.diagnostics.some((d) => d.severity === 'Warning');

    if (!hasFindings) {
      this.state = 'empty';
    } else if (isPartial) {
      this.state = 'partial';
    } else {
      this.state = 'success';
    }

    this.resultChanged.emit(response);
  }
}
