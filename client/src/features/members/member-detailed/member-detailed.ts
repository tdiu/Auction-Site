import { Component } from '@angular/core';
import {MemberService} from '../../../core/services/member-service';

@Component({
  selector: 'app-member-detailed',
  imports: [],
  templateUrl: './member-detailed.html',
  styleUrl: './member-detailed.css',
})
export class MemberDetailed {
  private memberService = new MemberService();
}
