import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StatusService } from './status.service';
import { PcapUploadComponent } from './pcap-upload.component/pcap-upload.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, PcapUploadComponent],
  templateUrl: './app.html',
  styleUrls: ['./app.css'],
})
export class App {
  constructor(private statusService: StatusService) {}

  protected readonly title = signal('protocol-lens-ui');
  statusMessage = '';

  collectResponse() {
    this.statusService.getStatus().subscribe({
      next: (response) => {
        this.statusMessage = response.message;
      },
    });
  }
}
