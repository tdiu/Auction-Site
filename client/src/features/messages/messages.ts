import {Component, inject, signal} from '@angular/core';
import {DatePipe} from '@angular/common';
import {MessageService} from '../../core/services/message-service';
import {Message} from '../../types/message';
import {PagedResponse} from '../../types/pagination';

type Container = 'Inbox' | 'Outbox';

@Component({
  selector: 'app-messages',
  imports: [DatePipe],
  templateUrl: './messages.html',
  styleUrl: './messages.css',
})
export class Messages {
  private messageService = inject(MessageService);

  protected container = signal<Container>('Inbox');
  protected pageNumber = signal(1);
  protected pageSize = 10;
  protected paginatedMessages = signal<PagedResponse<Message> | null>(null);

  protected selectedMessage = signal<Message | null>(null);

  ngOnInit() {
    this.loadMessages();
  }

  loadMessages() {
    this.messageService.getMessages(this.container(), this.pageNumber(), this.pageSize).subscribe({
      next: response => this.paginatedMessages.set(response),
    });
  }

  setContainer(container: Container) {
    if (this.container() === container) return;
    this.container.set(container);
    this.pageNumber.set(1);
    this.loadMessages();
  }

  goToPage(page: number) {
    this.pageNumber.set(page);
    this.loadMessages();
  }

  openMessage(message: Message) {
    this.selectedMessage.set(message);
  }

  closeMessage() {
    this.selectedMessage.set(null);
  }
}
