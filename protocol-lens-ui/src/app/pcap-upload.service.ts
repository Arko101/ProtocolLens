import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class PcapUploadService {
  constructor(private http: HttpClient) {}

  onUploadPcapFile(formData: FormData) {
    return this.http.post('http://localhost:5000/api/pcap', formData);
  }
}
