import { TestBed } from '@angular/core/testing';

import { CommTrnServiceService } from './comm-trn-service.service';

describe('CommTrnServiceService', () => {
  let service: CommTrnServiceService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CommTrnServiceService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
