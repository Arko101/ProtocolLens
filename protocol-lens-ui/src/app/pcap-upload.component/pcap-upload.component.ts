import { Component } from '@angular/core';
import { PcapUploadService } from '../pcap-upload.service';
@Component({
  selector: 'app-pcap-upload',
  imports: [],
  templateUrl: './pcap-upload.component.html',
  styleUrls: ['./pcap-upload.component.css'],
})
export class PcapUploadComponent {
  constructor(private pcapUploadService: PcapUploadService) {}
  selectedFile: File | null = null;

  onPcapFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;

    if (input.files && input.files.length > 0) {
      this.selectedFile = input.files[0];
      const formData = new FormData();
      formData.append('file', this.selectedFile);
      // console.log(formData.get('file')); // Log the file to the console
      this.pcapUploadService.onUploadPcapFile(formData).subscribe({
        next: (response) => {
          console.log(response);
        },
        error: (error) => {
          console.error(error);
        },
      });
    }
  }
}
