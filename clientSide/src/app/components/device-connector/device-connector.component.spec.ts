import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DeviceConnectorComponent } from './device-connector.component';

describe('DeviceConnectorComponent', () => {
  let component: DeviceConnectorComponent;
  let fixture: ComponentFixture<DeviceConnectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DeviceConnectorComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(DeviceConnectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
