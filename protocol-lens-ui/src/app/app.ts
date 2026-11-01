import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StatusService } from './status.service';
import { PcapUploadComponent } from './pcap-upload.component/pcap-upload.component';
import { AnalysisResultComponent } from './analysis-result.component/analysis-result.component';
import { AnalysisResponse } from './analysis-response.model';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, PcapUploadComponent, AnalysisResultComponent],
  templateUrl: './app.html',
  styleUrls: ['./app.css'],
})
export class App {
  constructor(private statusService: StatusService) {}

  protected readonly title = signal('protocol-lens-ui');
  statusMessage = '';
  analysisResult: AnalysisResponse | null = null;

  collectResponse() {
    this.statusService.getStatus().subscribe({
      next: (response) => {
        this.statusMessage = response.message;
      },
      error: () => {
        this.statusMessage = 'Unable to reach the API.';
      },
    });
  }

  onResultChanged(result: AnalysisResponse | null) {
    this.analysisResult = result;
  }
}
