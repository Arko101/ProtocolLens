import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PcapUploadComponent } from './pcap-upload.component';

describe('PcapUploadComponent', () => {
  let component: PcapUploadComponent;
  let fixture: ComponentFixture<PcapUploadComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PcapUploadComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PcapUploadComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
